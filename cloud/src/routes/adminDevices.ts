import type { IRequest } from "itty-router";
import { renderAdminShell } from "../lib/adminLayout";
import { findAdminById, requireAdminSession } from "../lib/auth";
import { escapeHtml } from "../lib/html";
import type { DeviceRow, Env, PairingCodeRow } from "../types";
import { confirmPairingCode } from "./pairing";

const ONLINE_WINDOW_MS = 90_000; // ~3x the 30s heartbeat cadence from routes/config.ts

function heartbeatBadge(row: DeviceRow): string {
  if (!row.last_heartbeat_at) {
    return '<span class="badge off"><span class="dot"></span>nikdy</span>';
  }
  const isOnline = Date.now() - new Date(row.last_heartbeat_at).getTime() < ONLINE_WINDOW_MS;
  const label = isOnline ? "online" : new Date(row.last_heartbeat_at).toLocaleString("cs-CZ");
  return `<span class="badge ${isOnline ? "on" : "off"}"><span class="dot"></span>${label}</span>`;
}

function devicesPageHtml(adminUsername: string, pending: PairingCodeRow[], devices: DeviceRow[]): string {
  const pendingRows = pending
    .map(
      (row) => `
      <tr>
        <td><code>${escapeHtml(row.code)}</code></td>
        <td>${new Date(row.created_at).toLocaleString("cs-CZ")}</td>
        <td>${new Date(row.expires_at).toLocaleString("cs-CZ")}</td>
        <td>
          <form class="inline" method="post" action="/admin/pair/confirm">
            <input type="hidden" name="code" value="${escapeHtml(row.code)}">
            <input type="text" name="deviceName" placeholder="Název zařízení">
            <button type="submit" class="small">Potvrdit</button>
          </form>
        </td>
      </tr>`,
    )
    .join("");

  const deviceRows = devices
    .map((row) => {
      const revoked = Boolean(row.revoked_at);
      return `
      <tr>
        <td>${escapeHtml(row.name ?? "(bez názvu)")} ${revoked ? '<span class="badge warn">odpojeno</span>' : ""}</td>
        <td>${new Date(row.paired_at).toLocaleString("cs-CZ")}</td>
        <td>${revoked ? "—" : heartbeatBadge(row)}</td>
        <td>${escapeHtml(row.last_status ?? "—")}</td>
        <td class="actions-row">
          <form class="inline" method="post" action="/admin/devices/${row.id}/rename">
            <input type="text" name="name" placeholder="nový název" value="${escapeHtml(row.name ?? "")}">
            <button type="submit" class="secondary small">Přejmenovat</button>
          </form>
          ${
            revoked
              ? ""
              : `<form method="post" action="/admin/devices/${row.id}/revoke" data-confirm="Opravdu odpojit zařízení ${escapeHtml(row.name ?? row.id)}? Přestane fungovat, dokud se znovu nespáruje.">
                   <button type="submit" class="danger small">Odpojit</button>
                 </form>`
          }
        </td>
      </tr>`;
    })
    .join("");

  const body = `
    <div class="panel">
      <h2>Čekající párování</h2>
      <p>Kód vygenerovaný fotokoutkem — potvrďte ho tady, aby se zařízení spárovalo.</p>
      <table>
        <thead><tr><th>Kód</th><th>Vytvořeno</th><th>Vyprší</th><th></th></tr></thead>
        <tbody>${pendingRows || '<tr><td colspan="4" class="empty">Žádné čekající kódy.</td></tr>'}</tbody>
      </table>
    </div>
    <div class="panel">
      <h2>Spárovaná zařízení</h2>
      <table>
        <thead><tr><th>Název</th><th>Spárováno</th><th>Stav</th><th>Poslední hlášení</th><th>Akce</th></tr></thead>
        <tbody>${deviceRows || '<tr><td colspan="5" class="empty">Zatím žádná spárovaná zařízení.</td></tr>'}</tbody>
      </table>
    </div>`;

  return renderAdminShell({ title: "Zařízení", active: "devices", adminUsername, body });
}

/**
 * GET /admin/devices — merges the old /admin/pair (pending pairing-code confirmation, spec §31/§36)
 * with a new paired-devices table (rename / revoke), so device lifecycle lives in one place instead
 * of only being reachable by editing the database directly.
 */
export async function adminDevicesPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const [{ results: pending }, { results: devices }, self] = await Promise.all([
    env.DB.prepare("SELECT * FROM photobooth_pairing_codes WHERE status = 'pending' ORDER BY created_at DESC").all<PairingCodeRow>(),
    env.DB.prepare("SELECT * FROM photobooth_devices ORDER BY (revoked_at IS NOT NULL), paired_at DESC").all<DeviceRow>(),
    findAdminById(env, session.adminId),
  ]);

  return new Response(devicesPageHtml(self?.username ?? "", pending, devices), {
    headers: { "content-type": "text/html; charset=utf-8" },
  });
}

/** GET /admin/pair — old URL, kept working as a redirect so existing bookmarks/links don't break. */
export function adminPairRedirect(request: Request) {
  return Response.redirect(new URL("/admin/devices", request.url).toString(), 303);
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

  return Response.redirect(new URL("/admin/devices", request.url).toString(), 303);
}

/** POST /admin/devices/:id/rename */
export async function renameDevice(request: IRequest, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const formData = await request.formData();
  const name = String(formData.get("name") ?? "").trim();

  await env.DB.prepare("UPDATE photobooth_devices SET name = ? WHERE id = ?").bind(name || null, request.params.id).run();

  return Response.redirect(new URL("/admin/devices", request.url).toString(), 303);
}

/** POST /admin/devices/:id/revoke — stops the device from authenticating (config/heartbeat/upload)
 * without deleting its row, since photos and events reference it by foreign key. */
export async function revokeDevice(request: IRequest, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  await env.DB
    .prepare("UPDATE photobooth_devices SET revoked_at = ? WHERE id = ?")
    .bind(new Date().toISOString(), request.params.id)
    .run();

  return Response.redirect(new URL("/admin/devices", request.url).toString(), 303);
}
