import { StatusError } from "itty-router";
import type { Env, PairingCodeRow } from "../types";
import { requireAdminKey } from "../lib/auth";

/**
 * GET /admin/pair?key=... — the "minimal development admin UI" spec §31 asks for: lists pending
 * pairing codes and lets a human confirm one. This is deliberately small — a real deployment wires
 * pairing confirmation into Camledian's existing administration instead (spec §56).
 */
export async function adminPairPage(request: Request, env: Env) {
  const failure = requireAdminKey(request, env);
  if (failure) {
    throw new StatusError(failure.status, { error: failure.error });
  }

  const url = new URL(request.url);
  const key = url.searchParams.get("key") ?? "";

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
          <form method="post" action="/admin/pair/confirm?key=${encodeURIComponent(key)}">
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
  <p><a href="/admin/stats?key=${encodeURIComponent(key)}">Statistiky &rarr;</a></p>
  <h1>Čekající párovací kódy</h1>
  <table>
    <thead><tr><th>Kód</th><th>Vytvořeno</th><th>Vyprší</th><th></th></tr></thead>
    <tbody>${rows || '<tr><td colspan="4">Žádné čekající kódy.</td></tr>'}</tbody>
  </table>
</body></html>`;

  return new Response(html, { headers: { "content-type": "text/html; charset=utf-8" } });
}

export async function adminPairConfirmForm(request: Request, env: Env) {
  const failure = requireAdminKey(request, env);
  if (failure) {
    throw new StatusError(failure.status, { error: failure.error });
  }

  const formData = await request.formData();
  const code = String(formData.get("code") ?? "");
  const deviceName = String(formData.get("deviceName") ?? "") || undefined;

  const jsonRequest = new Request(request.url, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ code, deviceName }),
  });

  const { pairConfirm } = await import("./pairing");
  await pairConfirm(jsonRequest, env);

  const url = new URL(request.url);
  const key = url.searchParams.get("key") ?? "";
  return Response.redirect(`${url.origin}/admin/pair?key=${encodeURIComponent(key)}`, 303);
}
