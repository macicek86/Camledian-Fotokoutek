import type { Env } from "../types";
import { requireAdminSession } from "../lib/auth";

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

const PAGE_SIZE = 100;

/**
 * GET /admin/gallery?event=... — login-protected thumbnail overview of uploaded photos, so staff
 * can look a guest's photo back up (e.g. a lost/expired QR link) without needing the original
 * download token. Distinct from the public /foto/:token page (spec §42), which never lists or
 * groups photos — this view exists purely for internal lookup, gated by the same admin session as
 * /admin/stats (see routes/adminAuth.ts).
 */
export async function adminGalleryPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const url = new URL(request.url);
  const eventFilter = url.searchParams.get("event") ?? "";

  const { results: events } = await env.DB
    .prepare("SELECT id, name FROM photobooth_events ORDER BY created_at DESC")
    .all<EventOptionRow>();

  const photosQuery = eventFilter
    ? env.DB
        .prepare(
          `SELECT p.id, e.name AS event_name, p.download_token, p.uploaded_at, p.expires_at
           FROM photobooth_photos p
           LEFT JOIN photobooth_events e ON e.id = p.event_id
           WHERE p.status = 'uploaded' AND p.event_id = ?
           ORDER BY p.uploaded_at DESC
           LIMIT ?`,
        )
        .bind(eventFilter, PAGE_SIZE)
    : env.DB
        .prepare(
          `SELECT p.id, e.name AS event_name, p.download_token, p.uploaded_at, p.expires_at
           FROM photobooth_photos p
           LEFT JOIN photobooth_events e ON e.id = p.event_id
           WHERE p.status = 'uploaded'
           ORDER BY p.uploaded_at DESC
           LIMIT ?`,
        )
        .bind(PAGE_SIZE);

  const { results: photos } = await photosQuery.all<GalleryRow>();

  const eventOptions = events
    .map(
      (event) =>
        `<option value="${event.id}" ${event.id === eventFilter ? "selected" : ""}>${event.name}</option>`,
    )
    .join("");

  const cards = photos
    .map((photo) => {
      const publicUrl = `/foto/${photo.download_token}`;
      const thumbUrl = `/foto/${photo.download_token}/file`;
      const uploaded = new Date(photo.uploaded_at).toLocaleString("cs-CZ");
      const expired = photo.expires_at && new Date(photo.expires_at).getTime() < Date.now();
      return `
      <a class="card" href="${publicUrl}" target="_blank" rel="noopener">
        <img src="${thumbUrl}" alt="" loading="lazy">
        <div class="meta">
          <span>${photo.event_name ?? "(bez akce)"}</span>
          <span>${uploaded}</span>
          ${expired ? '<span class="expired">vypršelo</span>' : ""}
        </div>
      </a>`;
    })
    .join("");

  const html = `<!doctype html>
<html lang="cs"><head><meta charset="utf-8"><title>Přehled fotografií</title>
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<style>
  body { font-family: system-ui, sans-serif; background: #0d1b2e; color: #f5f5f5; padding: 32px; }
  a { color: #d4af37; }
  select, button { padding: 6px 10px; font-size: 1rem; }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 16px; margin-top: 24px; }
  .card { display: block; background: #16283f; border-radius: 12px; overflow: hidden; text-decoration: none; color: #f5f5f5; }
  .card img { width: 100%; aspect-ratio: 1; object-fit: cover; display: block; background: #0d1b2e; }
  .meta { padding: 8px 10px; display: flex; flex-direction: column; gap: 2px; font-size: 0.85rem; color: #9fb0c3; }
  .meta span:first-child { color: #f5f5f5; font-weight: 600; }
  .expired { color: #e0a458; }
  .empty { color: #9fb0c3; margin-top: 24px; }
</style></head>
<body>
  <p><a href="/admin/pair">Párování zařízení</a> &middot; <a href="/admin/stats">Statistiky</a> &middot; <a href="/admin/users">Účty</a>
  <form style="display:inline; margin-left:12px" method="post" action="/admin/logout"><button type="submit">Odhlásit se</button></form></p>
  <h1>Přehled fotografií</h1>
  <form method="get">
    <select name="event" onchange="this.form.submit()">
      <option value="">Všechny akce</option>
      ${eventOptions}
    </select>
    <noscript><button type="submit">Filtrovat</button></noscript>
  </form>
  ${cards ? `<div class="grid">${cards}</div>` : '<p class="empty">Zatím žádné nahrané fotografie.</p>'}
</body></html>`;

  return new Response(html, { headers: { "content-type": "text/html; charset=utf-8" } });
}
