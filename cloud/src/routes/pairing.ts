import { StatusError, type IRequest } from "itty-router";
import type { Env, PairingCodeRow } from "../types";
import { createId, createDownloadToken, sha256Hex } from "../lib/ids";
import { requireAdminKey } from "../lib/auth";

const PAIRING_CODE_TTL_MINUTES = 10;
/** How long after confirmation the freshly minted device token stays collectable via
 * GET /pair/status. The window restarts at confirmation time (see confirmPairingCode) so it doesn't
 * matter how long the admin took to click "Potvrdit" — the app polls every 4s and picks the token up
 * within seconds, and retries still work, but the code stops being a way to mint a device identity
 * long after the fact. */
const TOKEN_COLLECTION_MINUTES = 5;
const PAIRING_CODE_PATTERN = /^[A-Z0-9]{4}-[A-Z0-9]{4}$/;

/** POST /api/photobooth/pair/start — the Windows app generates the human-readable code locally
 * (TokenGenerator.CreatePairingCode()) and registers it here as a pending request (spec §36). */
export async function pairStart(request: Request, env: Env) {
  const body = await request.json<{ code?: string }>().catch(() => ({}) as { code?: string });
  const code = (body.code ?? "").toUpperCase();
  if (!PAIRING_CODE_PATTERN.test(code)) {
    throw new StatusError(400, { error: "code must look like ABCD-1234." });
  }

  const now = new Date();
  const expiresAt = new Date(now.getTime() + PAIRING_CODE_TTL_MINUTES * 60_000);

  await env.DB
    .prepare(
      `INSERT INTO photobooth_pairing_codes (code, status, created_at, expires_at)
       VALUES (?, 'pending', ?, ?)
       ON CONFLICT(code) DO UPDATE SET status = 'pending', created_at = excluded.created_at, expires_at = excluded.expires_at, device_id = NULL, device_token = NULL, confirmed_at = NULL`,
    )
    .bind(code, now.toISOString(), expiresAt.toISOString())
    .run();

  return { code, expiresAt: expiresAt.toISOString(), pollIntervalSeconds: 4 };
}

/** GET /api/photobooth/pair/status/:code — the Windows app polls this until an admin confirms. */
export async function pairStatus(request: IRequest, env: Env) {
  const row = await env.DB
    .prepare("SELECT * FROM photobooth_pairing_codes WHERE code = ?")
    .bind(request.params.code!.toUpperCase())
    .first<PairingCodeRow>();

  if (!row) {
    throw new StatusError(404, { error: "Unknown pairing code." });
  }

  if (row.status === "pending" && new Date(row.expires_at).getTime() < Date.now()) {
    await env.DB.prepare("UPDATE photobooth_pairing_codes SET status = 'expired' WHERE code = ?").bind(row.code).run();
    return { status: "expired" };
  }

  if (row.status === "confirmed") {
    // The token is only handed out inside the post-confirmation collection window. Without this the
    // row kept serving a live device token forever, and the pairing code is shown on the photobooth's
    // own screen — anyone who ever saw it could come back later and mint themselves a device identity.
    if (new Date(row.expires_at).getTime() < Date.now()) {
      if (row.device_token) {
        await env.DB.prepare("UPDATE photobooth_pairing_codes SET device_token = NULL WHERE code = ?").bind(row.code).run();
      }
      return { status: "confirmed", deviceId: row.device_id, deviceToken: null };
    }

    return { status: "confirmed", deviceId: row.device_id, deviceToken: row.device_token };
  }

  return { status: row.status };
}

/** Core of pairing confirmation, shared by the admin-key-gated API route below and the
 * session-gated browser form in routes/adminAuth.ts/adminPairUi.ts — auth is each caller's own
 * responsibility, this just does the DB work once a caller has already been authorized.
 * SECURITY: this mints a real device token, so callers must always authorize before invoking it —
 * without that anyone could confirm their own pairing code and get a valid device identity. */
export async function confirmPairingCode(env: Env, rawCode: string, deviceName: string | undefined) {
  const code = rawCode.toUpperCase();

  const row = await env.DB
    .prepare("SELECT * FROM photobooth_pairing_codes WHERE code = ?")
    .bind(code)
    .first<PairingCodeRow>();

  if (!row || row.status !== "pending") {
    throw new StatusError(409, { error: "Pairing code is not pending confirmation." });
  }

  if (new Date(row.expires_at).getTime() < Date.now()) {
    throw new StatusError(410, { error: "Pairing code expired." });
  }

  const deviceId = createId();
  const deviceToken = createDownloadToken(40);
  const tokenHash = await sha256Hex(deviceToken);
  const now = new Date().toISOString();

  await env.DB
    .prepare("INSERT INTO photobooth_devices (id, name, token_hash, paired_at) VALUES (?, ?, ?, ?)")
    .bind(deviceId, deviceName ?? null, tokenHash, now)
    .run();

  // Restart the clock: expires_at now means "until when is the device token collectable", which is
  // what pairStatus above enforces.
  const collectableUntil = new Date(Date.now() + TOKEN_COLLECTION_MINUTES * 60_000).toISOString();

  await env.DB
    .prepare(
      "UPDATE photobooth_pairing_codes SET status = 'confirmed', device_id = ?, device_token = ?, confirmed_at = ?, expires_at = ? WHERE code = ?",
    )
    .bind(deviceId, deviceToken, now, collectableUntil, code)
    .run();

  return { deviceId, confirmed: true };
}

/** POST /api/photobooth/pair/confirm — the server-to-server surface, gated by ADMIN_API_KEY (spec
 * §56: a future Camledian-side integration could call this directly, without a browser session).
 * Human staff instead use the /admin/pair browser form, gated by a real login session — see
 * routes/adminAuth.ts and requireAdminSession. */
export async function pairConfirm(request: Request, env: Env) {
  const failure = await requireAdminKey(request, env);
  if (failure) {
    throw new StatusError(failure.status, { error: failure.error });
  }

  const body = await request
    .json<{ code?: string; deviceName?: string }>()
    .catch(() => ({}) as { code?: string; deviceName?: string });

  return confirmPairingCode(env, body.code ?? "", body.deviceName);
}
