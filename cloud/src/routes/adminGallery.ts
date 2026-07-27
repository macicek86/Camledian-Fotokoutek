import type { IRequest } from "itty-router";
import { filmStripDivider, renderAdminShell, renderPhotoCard } from "../lib/adminLayout";
import { findAdminById, requireAdminSession } from "../lib/auth";
import { escapeHtml } from "../lib/html";
import { expirePhoto } from "../lib/photos";
import type { Env } from "../types";

interface GalleryRow {
  id: string;
  event_name: string | null;
  download_token: string;
  uploaded_at: string;
  expires_at: string | null;
}

interface EventOptionRow {
  id: string;
  name: string;
}

const PAGE_SIZE = 60;

function buildUrl(base: string, params: Record<string, string>): string {
  const url = new URL(base, "https://x");
  for (const [key, value] of Object.entries(params)) {
    if (value) url.searchParams.set(key, value);
  }
  return `${url.pathname}${url.search}`;
}

/**
 * GET /admin/gallery?event=&from=&to=&page= — login-protected thumbnail overview of uploaded
 * photos, so staff can look a guest's photo back up (e.g. a lost/expired QR link) without needing
 * the original download token, and can remove a photo on request (e.g. a guest asking to have theirs
 * taken down). Distinct from the public /foto/:token page (spec §42), which never lists or groups
 * photos — this view exists purely for internal lookup, gated by the same admin session as
 * /admin/stats (see routes/adminAuth.ts).
 */
export async function adminGalleryPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const url = new URL(request.url);
  const eventFilter = url.searchParams.get("event") ?? "";
  const fromFilter = url.searchParams.get("from") ?? "";
  const toFilter = url.searchParams.get("to") ?? "";
  const page = Math.max(1, Number(url.searchParams.get("page")) || 1);

  const conditions = ["p.status = 'uploaded'"];
  const params: unknown[] = [];
  if (eventFilter) {
    conditions.push("p.event_id = ?");
    params.push(eventFilter);
  }
  if (fromFilter) {
    conditions.push("p.uploaded_at >= ?");
    params.push(`${fromFilter}T00:00:00.000Z`);
  }
  if (toFilter) {
    conditions.push("p.uploaded_at <= ?");
    params.push(`${toFilter}T23:59:59.999Z`);
  }

  const [{ results: events }, { results: rows }, self] = await Promise.all([
    env.DB.prepare("SELECT id, name FROM photobooth_events ORDER BY created_at DESC").all<EventOptionRow>(),
    env.DB
      .prepare(
        `SELECT p.id, e.name AS event_name, p.download_token, p.uploaded_at, p.expires_at
         FROM photobooth_photos p
         LEFT JOIN photobooth_events e ON e.id = p.event_id
         WHERE ${conditions.join(" AND ")}
         ORDER BY p.uploaded_at DESC
         LIMIT ? OFFSET ?`,
      )
      .bind(...params, PAGE_SIZE + 1, (page - 1) * PAGE_SIZE)
      .all<GalleryRow>(),
    findAdminById(env, session.adminId),
  ]);

  const hasNext = rows.length > PAGE_SIZE;
  const photos = rows.slice(0, PAGE_SIZE);

  const eventOptions = events
    .map((event) => `<option value="${event.id}" ${event.id === eventFilter ? "selected" : ""}>${escapeHtml(event.name)}</option>`)
    .join("");

  const cards = photos
    .map((photo) => {
      const expired = Boolean(photo.expires_at && new Date(photo.expires_at).getTime() < Date.now());
      return renderPhotoCard({
        thumbUrl: `/foto/${photo.download_token}/file`,
        publicUrl: `/foto/${photo.download_token}`,
        eventName: photo.event_name ?? "(bez akce)",
        uploadedLabel: new Date(photo.uploaded_at).toLocaleString("cs-CZ"),
        expired,
        deleteUrl: `/admin/gallery/${photo.id}/delete`,
      });
    })
    .join("");

  const filterState = { event: eventFilter, from: fromFilter, to: toFilter };
  const pagination = `
    <div class="pagination">
      ${page > 1 ? `<a class="btn small secondary" href="${buildUrl("/admin/gallery", { ...filterState, page: String(page - 1) })}">&larr; Novější</a>` : ""}
      <span style="align-self:center; color:var(--text-muted); font-size:0.85rem">strana ${page}</span>
      ${hasNext ? `<a class="btn small secondary" href="${buildUrl("/admin/gallery", { ...filterState, page: String(page + 1) })}">Starší &rarr;</a>` : ""}
    </div>`;

  const body = `
    ${filmStripDivider()}
    <div class="panel">
      <form class="filters" method="get">
        <div>
          <label for="event">Akce</label>
          <select id="event" name="event"><option value="">Všechny akce</option>${eventOptions}</select>
        </div>
        <div>
          <label for="from">Od</label>
          <input type="date" id="from" name="from" value="${escapeHtml(fromFilter)}">
        </div>
        <div>
          <label for="to">Do</label>
          <input type="date" id="to" name="to" value="${escapeHtml(toFilter)}">
        </div>
        <button type="submit">Filtrovat</button>
      </form>
    </div>
    ${
      cards
        ? `<div class="photo-grid">${cards}</div>${pagination}`
        : `<p class="empty">${eventFilter || fromFilter || toFilter ? "Žádné fotografie neodpovídají filtru." : "Zatím žádné nahrané fotografie."}</p>`
    }`;

  return new Response(renderAdminShell({ title: "Galerie", active: "gallery", adminUsername: self?.username ?? "", body }), {
    headers: { "content-type": "text/html; charset=utf-8" },
  });
}

/** POST /admin/gallery/:id/delete — removes a photo on request (e.g. a guest asking it be taken
 * down): drops the R2 bytes and marks the row expired via the shared expirePhoto() helper (same
 * semantics the daily retention cleanup uses), rather than hard-deleting the row. */
export async function deleteGalleryPhoto(request: IRequest, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const photo = await env.DB
    .prepare("SELECT id, r2_key FROM photobooth_photos WHERE id = ?")
    .bind(request.params.id)
    .first<{ id: string; r2_key: string }>();

  if (photo) {
    await expirePhoto(env, photo);
  }

  // "Bounce back to whichever admin page the delete came from", but the origin has to match too:
  // checking only the path let https://evil.example/admin/x through as a redirect target.
  return Response.redirect(safeAdminReferer(request) ?? new URL("/admin/gallery", request.url).toString(), 303);
}

function safeAdminReferer(request: Request): string | null {
  const referer = request.headers.get("referer");
  if (!referer) return null;

  try {
    const target = new URL(referer);
    const self = new URL(request.url);
    return target.origin === self.origin && target.pathname.startsWith("/admin/") ? target.toString() : null;
  } catch {
    return null;
  }
}
