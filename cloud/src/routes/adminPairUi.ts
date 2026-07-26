import type { Env, PairingCodeRow } from "../types";
import { requireAdminSession } from "../lib/auth";
import { confirmPairingCode } from "./pairing";

function adminNav(): string {
  return `<p>
    <a href="/admin/stats">Statistiky</a> &middot;
    <a href="/admin/gallery">Přehled fotografií</a> &middot;
    <a href="/admin/users">Účty</a>
    <form style="display:inline; margin-left:12px" method="post" action="/admin/logout"><button type="submit">Odhlásit se</button></form>
  </p>`;
}

/**
 * GET /admin/pair — lists pending pairing codes and lets a logged-in admin confirm one (spec §31
 * "minimální development admin UI"). Gated by a real login session (see routes/adminAuth.ts) rather
 * than a shared key — a real deployment may eventually wire pairing confirmation into Camledian's
 * existing shop/POS administration instead (spec §56), but that's not happening yet, so this needs
 * to work as its own standalone system.
 */
export async function adminPairPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const { results } = await env.DB
    .prepare("SELECT * FROM photobooth_pairing_codes WHERE status = 'pending' ORDER BY created_at DESC")
    .all<PairingCodeRow>();

  const rows = results
    .map(
      (row) => `
      <tr>
        <td><code>${row.code}</code></td>
        <td>${new Date(row.created_at).toLocaleString("cs-CZ")}</td>
        <td>${new Date(row.expires_at).toLocaleString("cs-CZ")}</td>
        <td>
          <form method="post" action="/admin/pair/confirm">
            <input type="hidden" name="code" value="${row.code}">
            <input type="text" name="deviceName" placeholder="Název zařízení" />
            <button type="submit">Potvrdit</button>
          </form>
        </td>
      </tr>`,
    )
    .join("");

  const html = `<!doctype html>
<html lang="cs"><head><meta charset="utf-8"><title>Párování zařízení</title>
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<style>
  body { font-family: system-ui, sans-serif; background: #0d1b2e; color: #f5f5f5; padding: 32px; }
  a { color: #d4af37; }
  table { border-collapse: collapse; width: 100%; }
  td, th { padding: 8px 12px; border-bottom: 1px solid #223650; text-align: left; }
  code { font-size: 1.2rem; }
  input, button { padding: 6px 10px; font-size: 1rem; }
  button { background: #d4af37; color: #0d1b2e; border: none; border-radius: 6px; cursor: pointer; font-weight: 700; }
</style></head>
<body>
  ${adminNav()}
  <h1>Čekající párovací kódy</h1>
  <table>
    <thead><tr><th>Kód</th><th>Vytvořeno</th><th>Vyprší</th><th></th></tr></thead>
    <tbody>${rows || '<tr><td colspan="4">Žádné čekající kódy.</td></tr>'}</tbody>
  </table>
</body></html>`;

  return new Response(html, { headers: { "content-type": "text/html; charset=utf-8" } });
}

/** POST /admin/pair/confirm — browser form counterpart to POST /api/photobooth/pair/confirm; shares
 * the same confirmPairingCode() core but is gated by the admin's login session instead of the key. */
export async function adminPairConfirmForm(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const formData = await request.formData();
  const code = String(formData.get("code") ?? "");
  const deviceName = String(formData.get("deviceName") ?? "") || undefined;

  await confirmPairingCode(env, code, deviceName);

  return Response.redirect(new URL("/admin/pair", request.url).toString(), 303);
}
