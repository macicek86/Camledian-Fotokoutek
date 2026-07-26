import { StatusError } from "itty-router";
import type { Env, EventRow } from "../types";
import { requireDeviceAuth } from "../lib/auth";

/** GET /api/photobooth/config — a device's assigned event plus sync/heartbeat cadence (spec §32). */
export async function getConfig(request: Request, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  // Prefer an event assigned specifically to this device; fall back to a shared/default event.
  const event = await env.DB
    .prepare(
      `SELECT * FROM photobooth_events
       WHERE is_active = 1 AND (device_id = ? OR device_id IS NULL)
       ORDER BY (device_id IS NULL) ASC
       LIMIT 1`,
    )
    .bind(auth.deviceId)
    .first<EventRow>();

  return {
    deviceId: auth.deviceId,
    event: event
      ? { id: event.id, name: event.name, outputTemplateId: event.output_template_id }
      : null,
    sync: {
      syncIntervalSeconds: 60,
      heartbeatIntervalSeconds: 30,
    },
    galleryBaseUrl: env.GALLERY_BASE_URL,
  };
}
