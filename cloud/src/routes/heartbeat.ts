import { StatusError } from "itty-router";
import type { Env } from "../types";
import { requireDeviceAuth } from "../lib/auth";

/** POST /api/photobooth/heartbeat — spec §32/§44 ("Devices" management, online status). */
export async function heartbeat(request: Request, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  const body = await request.json<{ status?: string }>().catch(() => ({}) as { status?: string });
  const now = new Date().toISOString();

  await env.DB
    .prepare("UPDATE photobooth_devices SET last_heartbeat_at = ?, last_status = ? WHERE id = ?")
    .bind(now, body.status ?? "online", auth.deviceId)
    .run();

  return { ok: true, serverTimeUtc: now };
}
