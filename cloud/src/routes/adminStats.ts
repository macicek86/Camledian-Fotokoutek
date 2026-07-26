import type { Env } from "../types";
import { requireAdminSession } from "../lib/auth";

interface EventStatsRow {
  event_id: string | null;
  event_name: string | null;
  total: number;
  uploaded: number;
  pending: number;
}

/**
 * GET /admin/stats — internal, login-protected overview of photo counts grouped by event (spec §44
 * "statistiky"). Deliberately separate from the public /foto/:token page, which only ever shows one
 * photo to whoever holds its unguessable token — there is no public listing or grouping of photos
 * by event/location, to protect guests' privacy.
 */
export async function adminStatsPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const { results: eventStats } = await env.DB
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
    .all<EventStatsRow>();

  const unassigned = await env.DB
    .prepare(
      `SELECT
         COUNT(*) AS total,
         SUM(CASE WHEN status = 'uploaded' THEN 1 ELSE 0 END) AS uploaded,
         SUM(CASE WHEN status = 'pending-upload' THEN 1 ELSE 0 END) AS pending
       FROM photobooth_photos WHERE event_id IS NULL`,
    )
    .first<{ total: number; uploaded: number; pending: number }>();

  const totals = await env.DB
    .prepare(
      `SELECT
         (SELECT COUNT(*) FROM photobooth_devices) AS devices,
         (SELECT COUNT(*) FROM photobooth_events) AS events,
         (SELECT COUNT(*) FROM photobooth_photos WHERE status = 'uploaded') AS uploadedPhotos`,
    )
    .first<{ devices: number; events: number; uploadedPhotos: number }>();

  const eventRows = eventStats
    .map(
      (row) => `
      <tr>
        <td>${row.event_name ?? "(bez názvu)"}</td>
        <td>${row.total}</td>
        <td>${row.uploaded}</td>
        <td>${row.pending}</td>
      </tr>`,
    )
    .join("");

  const html = `<!doctype html>
<html lang="cs"><head><meta charset="utf-8"><title>Statistiky</title>
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<style>
  body { font-family: system-ui, sans-serif; background: #0d1b2e; color: #f5f5f5; padding: 32px; }
  a { color: #d4af37; }
  table { border-collapse: collapse; width: 100%; margin-top: 16px; }
  td, th { padding: 8px 12px; border-bottom: 1px solid #223650; text-align: left; }
  .totals { display: flex; gap: 24px; margin: 16px 0 32px; }
  .totals div { background: #16283f; border-radius: 12px; padding: 16px 24px; }
  .totals strong { display: block; font-size: 1.6rem; color: #d4af37; }
</style></head>
<body>
  <p><a href="/admin/pair">&larr; Párování zařízení</a> &middot; <a href="/admin/gallery">Přehled fotografií</a> &middot; <a href="/admin/users">Účty</a>
  <form style="display:inline; margin-left:12px" method="post" action="/admin/logout"><button type="submit">Odhlásit se</button></form></p>
  <h1>Statistiky fotokoutku</h1>
  <div class="totals">
    <div><strong>${totals?.devices ?? 0}</strong>zařízení</div>
    <div><strong>${totals?.events ?? 0}</strong>akcí</div>
    <div><strong>${totals?.uploadedPhotos ?? 0}</strong>nahraných fotek</div>
  </div>
  <h2>Podle akce</h2>
  <table>
    <thead><tr><th>Akce</th><th>Celkem</th><th>Nahráno</th><th>Čeká na upload</th></tr></thead>
    <tbody>
      ${eventRows || '<tr><td colspan="4">Zatím žádné akce.</td></tr>'}
      ${
        unassigned && unassigned.total > 0
          ? `<tr><td><em>Nezařazeno</em></td><td>${unassigned.total}</td><td>${unassigned.uploaded}</td><td>${unassigned.pending}</td></tr>`
          : ""
      }
    </tbody>
  </table>
</body></html>`;

  return new Response(html, { headers: { "content-type": "text/html; charset=utf-8" } });
}
