import { StatusError, type IRequest } from "itty-router";
import type { AssetRow, Env, EventRow } from "../types";
import { requireDeviceAuth } from "../lib/auth";

/** GET /api/photobooth/events */
export async function listEvents(request: IRequest, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  const { results } = await env.DB
    .prepare("SELECT * FROM photobooth_events WHERE device_id = ? OR device_id IS NULL ORDER BY created_at DESC")
    .bind(auth.deviceId)
    .all<EventRow>();

  return {
    events: results.map((e) => ({
      id: e.id,
      name: e.name,
      outputTemplateId: e.output_template_id,
      isActive: e.is_active === 1,
    })),
  };
}

/** GET /api/photobooth/events/:id */
export async function getEvent(request: IRequest, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  const event = await env.DB
    .prepare("SELECT * FROM photobooth_events WHERE id = ? AND (device_id = ? OR device_id IS NULL)")
    .bind(request.params.id, auth.deviceId)
    .first<EventRow>();

  if (!event) {
    throw new StatusError(404, { error: "Event not found." });
  }

  return { id: event.id, name: event.name, outputTemplateId: event.output_template_id, isActive: event.is_active === 1 };
}

/** GET /api/photobooth/events/:id/assets — the manifest CloudSyncService diffs by hash (spec §37). */
export async function getEventAssets(request: IRequest, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  const eventId = request.params.id;
  const event = await env.DB
    .prepare("SELECT id FROM photobooth_events WHERE id = ? AND (device_id = ? OR device_id IS NULL)")
    .bind(eventId, auth.deviceId)
    .first<{ id: string }>();
  if (!event) {
    throw new StatusError(404, { error: "Event not found." });
  }

  const [backgrounds, overlays] = await Promise.all([
    env.DB.prepare("SELECT * FROM photobooth_backgrounds WHERE event_id = ? ORDER BY sort_order").bind(eventId).all<AssetRow>(),
    env.DB.prepare("SELECT * FROM photobooth_overlays WHERE event_id = ? ORDER BY sort_order").bind(eventId).all<AssetRow>(),
  ]);

  const url = new URL(request.url);
  const toEntry = (row: AssetRow, type: "background" | "overlay") => ({
    id: row.id,
    type,
    name: row.name,
    hash: row.hash,
    url: `${url.origin}/assets/${row.id}`,
  });

  const assets = [
    ...backgrounds.results.map((r) => toEntry(r, "background")),
    ...overlays.results.map((r) => toEntry(r, "overlay")),
  ];

  return {
    version: assets.length,
    assets,
  };
}

/** GET /assets/:id — proxies the actual bytes out of R2 so manifest URLs work without exposing the
 * bucket publicly or configuring a custom domain. */
export async function getAssetFile(request: IRequest, env: Env) {
  const id = request.params.id;
  const asset =
    (await env.DB.prepare("SELECT r2_key FROM photobooth_backgrounds WHERE id = ?").bind(id).first<{ r2_key: string }>()) ??
    (await env.DB.prepare("SELECT r2_key FROM photobooth_overlays WHERE id = ?").bind(id).first<{ r2_key: string }>());

  if (!asset) {
    throw new StatusError(404, { error: "Asset not found." });
  }

  const object = await env.ASSETS_BUCKET.get(asset.r2_key);
  if (!object) {
    throw new StatusError(404, { error: "Asset file missing from storage." });
  }

  return new Response(object.body, {
    headers: {
      "content-type": object.httpMetadata?.contentType ?? "application/octet-stream",
      "cache-control": "public, max-age=86400",
      etag: object.httpEtag,
    },
  });
}
