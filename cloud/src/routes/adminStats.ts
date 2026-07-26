import { filmStripDivider, renderAdminShell, renderPhotoCard } from "../lib/adminLayout";
import { findAdminById, requireAdminSession } from "../lib/auth";
import { escapeHtml } from "../lib/html";
import type { Env } from "../types";

interface EventStatsRow {
  event_id: string | null;
  event_name: string | null;
  total: number;
  uploaded: number;
  pending: number;
}

interface RecentPhotoRow {
  id: string;
  event_name: string | null;
  download_token: string;
  uploaded_at: string;
}

const RECENT_LIMIT = 8;

/**
 * GET /admin/stats — login-protected overview of photo counts grouped by event (spec §44
 * "statistiky"), doubling as the admin console's dashboard/landing page. Deliberately separate from
 * the public /foto/:token page, which only ever shows one photo to whoever holds its unguessable
 * token — there is no public listing or grouping of photos by event/location, to protect guests'
 * privacy.
 */
export async function adminStatsPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const [{ results: eventStats }, unassigned, totals, { results: recent }, self] = await Promise.all([
    env.DB
      .prepare(
        `SELECT
           e.id AS event_id,
           e.name AS event_name,
           COUNT(p.id) AS total,
           SUM(CASE WHEN p.status = 'uploaded' THEN 1 ELSE 0 END) AS uploaded,
           SUM(CASE WHEN p.status = 'pending-upload' THEN 1 ELSE 0 END) AS pending
         FROM photobooth_events e
         LEFT JOIN photobooth_photos p ON p.event_id = e.id
         GROUP BY e.id
         ORDER BY e.created_at DESC`,
      )
      .all<EventStatsRow>(),
    env.DB
      .prepare(
        `SELECT
           COUNT(*) AS total,
           SUM(CASE WHEN status = 'uploaded' THEN 1 ELSE 0 END) AS uploaded,
           SUM(CASE WHEN status = 'pending-upload' THEN 1 ELSE 0 END) AS pending
         FROM photobooth_photos WHERE event_id IS NULL`,
      )
      .first<{ total: number; uploaded: number; pending: number }>(),
    env.DB
      .prepare(
        `SELECT
           (SELECT COUNT(*) FROM photobooth_devices WHERE revoked_at IS NULL) AS devices,
           (SELECT COUNT(*) FROM photobooth_events) AS events,
           (SELECT COUNT(*) FROM photobooth_photos WHERE status = 'uploaded') AS uploadedPhotos`,
      )
      .first<{ devices: number; events: number; uploadedPhotos: number }>(),
    env.DB
      .prepare(
        `SELECT p.id, e.name AS event_name, p.download_token, p.uploaded_at
         FROM photobooth_photos p
         LEFT JOIN photobooth_events e ON e.id = p.event_id
         WHERE p.status = 'uploaded'
         ORDER BY p.uploaded_at DESC
         LIMIT ?`,
      )
      .bind(RECENT_LIMIT)
      .all<RecentPhotoRow>(),
    findAdminById(env, session.adminId),
  ]);

  const eventRows = eventStats
    .map(
      (row) => `
      <tr>
        <td>${escapeHtml(row.event_name ?? "(bez názvu)")}</td>
        <td class="num">${row.total}</td>
        <td class="num">${row.uploaded}</td>
        <td class="num">${row.pending}</td>
      </tr>`,
    )
    .join("");

  const recentCards = recent
    .map((photo) =>
      renderPhotoCard({
        thumbUrl: `/foto/${photo.download_token}/file`,
        publicUrl: `/foto/${photo.download_token}`,
        eventName: photo.event_name ?? "(bez akce)",
        uploadedLabel: new Date(photo.uploaded_at).toLocaleString("cs-CZ"),
        expired: false,
      }),
    )
    .join("");

  const body = `
    ${filmStripDivider()}
    <div class="stat-row">
      <div class="stat-card"><strong>${totals?.devices ?? 0}</strong><span>spárovaných zařízení</span></div>
      <div class="stat-card"><strong>${totals?.events ?? 0}</strong><span>akcí</span></div>
      <div class="stat-card"><strong>${totals?.uploadedPhotos ?? 0}</strong><span>nahraných fotek</span></div>
    </div>
    <div class="panel">
      <h2>Poslední nahrané fotky</h2>
      <div class="photo-grid">${recentCards || '<p class="empty">Zatím žádné nahrané fotografie.</p>'}</div>
    </div>
    <div class="panel">
      <h2>Podle akce</h2>
      <div class="table-scroll">
        <table>
          <thead><tr><th>Akce</th><th>Celkem</th><th>Nahráno</th><th>Čeká na upload</th></tr></thead>
          <tbody>
            ${eventRows || '<tr><td colspan="4" class="empty">Zatím žádné akce.</td></tr>'}
            ${
              unassigned && unassigned.total > 0
                ? `<tr><td><em>Nezařazeno</em></td><td class="num">${unassigned.total}</td><td class="num">${unassigned.uploaded}</td><td class="num">${unassigned.pending}</td></tr>`
                : ""
            }
          </tbody>
        </table>
      </div>
    </div>`;

  return new Response(renderAdminShell({ title: "Přehled", active: "stats", adminUsername: self?.username ?? "", body }), {
    headers: { "content-type": "text/html; charset=utf-8" },
  });
}
