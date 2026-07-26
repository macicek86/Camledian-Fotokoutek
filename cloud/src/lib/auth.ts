import type { Env } from "../types";
import { sha256Hex } from "./ids";

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

/** Shared-secret check for the small dev pairing-confirmation UI (spec §31: "minimální development
 * admin UI"). Meant to be replaced by real Camledian administration auth later — see spec §56. */
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
