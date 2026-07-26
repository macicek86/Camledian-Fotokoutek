import type { AdminRow, Env } from "../types";
import { createDownloadToken, sha256Hex } from "./ids";

export const ADMIN_SESSION_COOKIE = "admin_session";
const SESSION_TTL_HOURS = 12;

export interface AdminSessionResult {
  ok: true;
  adminId: string;
}

export interface DeviceAuthResult {
  ok: true;
  deviceId: string;
}

export interface AuthFailure {
  ok: false;
  status: number;
  error: string;
}

/** Verifies the `Authorization: Bearer <deviceToken>` header against the hashed token stored for a
 * paired device (spec §36). The plaintext token only ever exists on the wire and in the Windows
 * app's credential store — the database only ever sees its hash. */
export async function requireDeviceAuth(request: Request, env: Env): Promise<DeviceAuthResult | AuthFailure> {
  const header = request.headers.get("authorization") ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(header);
  if (!match) {
    return { ok: false, status: 401, error: "Missing Authorization: Bearer <deviceToken> header." };
  }

  const token = match[1]!;
  const tokenHash = await sha256Hex(token);
  const device = await env.DB
    .prepare("SELECT id FROM photobooth_devices WHERE token_hash = ?")
    .bind(tokenHash)
    .first<{ id: string }>();

  if (!device) {
    return { ok: false, status: 401, error: "Invalid or revoked device token." };
  }

  return { ok: true, deviceId: device.id };
}

/** Shared-secret check, now scoped to server-to-server / bootstrap use only: creating the very
 * first admin account (`POST /admin/setup`) and the device-pairing API
 * (`POST /api/photobooth/pair/confirm`), which a future Camledian-side integration could call
 * directly without a browser session. Human staff use the real login at `/admin/login` instead —
 * see requireAdminSession below. */
export function requireAdminKey(request: Request, env: Env): AuthFailure | null {
  if (!env.ADMIN_API_KEY) {
    return { ok: false, status: 503, error: "ADMIN_API_KEY is not configured on this deployment." };
  }

  const url = new URL(request.url);
  const provided = request.headers.get("x-admin-key") ?? url.searchParams.get("key");
  if (provided !== env.ADMIN_API_KEY) {
    return { ok: false, status: 401, error: "Invalid admin key." };
  }

  return null;
}

function parseCookie(request: Request, name: string): string | null {
  const header = request.headers.get("cookie") ?? "";
  for (const part of header.split(";")) {
    const [key, ...rest] = part.trim().split("=");
    if (key === name) {
      return rest.join("=");
    }
  }
  return null;
}

/** Mints a new session for a just-authenticated admin: a long random token, stored only as its
 * SHA-256 hash (same pattern as device tokens, see ids.ts) so a stolen DB dump can't be replayed as
 * a live session. Returns the `Set-Cookie` header value to send back to the browser. */
export async function createAdminSession(env: Env, adminId: string): Promise<string> {
  const token = createDownloadToken(40);
  const tokenHash = await sha256Hex(token);
  const now = new Date();
  const expiresAt = new Date(now.getTime() + SESSION_TTL_HOURS * 3_600_000);

  await env.DB
    .prepare("INSERT INTO photobooth_admin_sessions (token_hash, admin_id, created_at, expires_at) VALUES (?, ?, ?, ?)")
    .bind(tokenHash, adminId, now.toISOString(), expiresAt.toISOString())
    .run();

  // Secure is safe even for local `wrangler dev` on http://localhost — Chrome/Firefox both treat
  // localhost as a secure context, so they'll still set/send this cookie there.
  const maxAge = SESSION_TTL_HOURS * 3600;
  return `${ADMIN_SESSION_COOKIE}=${token}; HttpOnly; Secure; SameSite=Lax; Path=/admin; Max-Age=${maxAge}`;
}

/** Verifies the admin session cookie against the (hashed) session table, rejecting expired
 * sessions. Unlike requireAdminKey this identifies an actual account, is per-browser revocable
 * (logout deletes the row), and never appears in a URL, referrer header, or server access log. */
export async function requireAdminSession(request: Request, env: Env): Promise<AdminSessionResult | AuthFailure> {
  const token = parseCookie(request, ADMIN_SESSION_COOKIE);
  if (!token) {
    return { ok: false, status: 401, error: "Not logged in." };
  }

  const tokenHash = await sha256Hex(token);
  const session = await env.DB
    .prepare("SELECT admin_id, expires_at FROM photobooth_admin_sessions WHERE token_hash = ?")
    .bind(tokenHash)
    .first<{ admin_id: string; expires_at: string }>();

  if (!session || new Date(session.expires_at).getTime() < Date.now()) {
    return { ok: false, status: 401, error: "Session expired or invalid." };
  }

  return { ok: true, adminId: session.admin_id };
}

/** Looks up the admin account for a username, used by the login form. */
export async function findAdminByUsername(env: Env, username: string): Promise<AdminRow | null> {
  return env.DB.prepare("SELECT * FROM photobooth_admins WHERE username = ?").bind(username).first<AdminRow>();
}

export function clearAdminSessionCookie(): string {
  return `${ADMIN_SESSION_COOKIE}=; HttpOnly; Secure; SameSite=Lax; Path=/admin; Max-Age=0`;
}
