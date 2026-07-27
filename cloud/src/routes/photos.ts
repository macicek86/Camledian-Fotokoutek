import { StatusError, type IRequest } from "itty-router";
import type { Env, PhotoRow } from "../types";
import { requireDeviceAuth } from "../lib/auth";
import { createDownloadToken, createId } from "../lib/ids";

// A Map, not an object literal: `ALLOWED_CONTENT_TYPES["constructor"]` on a plain object walks the
// prototype chain and returns a function, which would sail past the `if (!extension)` check below.
const ALLOWED_CONTENT_TYPES = new Map<string, string>([
  ["image/jpeg", "jpg"],
  ["image/png", "png"],
]);

/** Used when DEFAULT_PHOTO_RETENTION_DAYS is missing or not a number — without a fallback,
 * `Number(undefined)` is NaN, `NaN > 0` is false, and photos would silently be stored with no expiry
 * at all, i.e. kept forever. */
const FALLBACK_RETENTION_DAYS = 30;

/** POST /api/photobooth/photos — registers an upload and returns the Worker-hosted PUT URL below
 * (spec §34, §39). The device PUTs the file straight through the Worker's R2 binding — no separate
 * R2 S3 API credentials needed, unlike a presigned-URL approach, at the cost of the file's bytes
 * passing through the Worker instead of going device-to-R2 directly. Fine for photobooth-sized
 * single-file uploads; env.ASSETS_BUCKET.put() streams the request body rather than buffering it. */
export async function createPhoto(request: IRequest, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  const body = await request
    .json<{ eventId?: string; contentType?: string }>()
    .catch(() => ({}) as { eventId?: string; contentType?: string });
  const contentType = body.contentType ?? "image/jpeg";
  const extension = ALLOWED_CONTENT_TYPES.get(contentType);
  if (!extension) {
    throw new StatusError(400, { error: `Unsupported contentType '${contentType}'. Use image/jpeg or image/png.` });
  }

  const photoId = createId();
  const r2Key = `photos/${auth.deviceId}/${photoId}.${extension}`;
  const now = new Date();
  const configured = Number(env.DEFAULT_PHOTO_RETENTION_DAYS);
  const retentionDays = Number.isFinite(configured) ? configured : FALLBACK_RETENTION_DAYS;
  const expiresAt = retentionDays > 0 ? new Date(now.getTime() + retentionDays * 86_400_000).toISOString() : null;

  await env.DB
    .prepare(
      `INSERT INTO photobooth_photos (id, device_id, event_id, r2_key, content_type, status, created_at, expires_at)
       VALUES (?, ?, ?, ?, ?, 'pending-upload', ?, ?)`,
    )
    .bind(photoId, auth.deviceId, body.eventId ?? null, r2Key, contentType, now.toISOString(), expiresAt)
    .run();

  return {
    photoId,
    uploadUrl: `/api/photobooth/photos/${photoId}/upload`,
    method: "PUT",
    requiredHeaders: { "content-type": contentType },
  };
}

/** PUT /api/photobooth/photos/:id/upload — receives the raw file body and streams it into R2 via
 * the native binding (spec §34/§39). */
export async function uploadPhoto(request: IRequest, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  const photo = await env.DB
    .prepare("SELECT * FROM photobooth_photos WHERE id = ?")
    .bind(request.params.id)
    .first<PhotoRow>();

  if (!photo || photo.device_id !== auth.deviceId) {
    throw new StatusError(404, { error: "Photo not found." });
  }

  // Upload slots are single-use: once a photo is uploaded its download token is already out in the
  // world on a QR code, so letting a later PUT swap the bytes underneath that link would silently
  // change what a guest sees. Retention-expired rows are equally off limits.
  if (photo.status !== "pending-upload") {
    throw new StatusError(409, { error: `Photo is already '${photo.status}' and can no longer be uploaded to.` });
  }

  if (!request.body) {
    throw new StatusError(400, { error: "Request body is empty." });
  }

  await env.ASSETS_BUCKET.put(photo.r2_key, request.body, {
    httpMetadata: { contentType: photo.content_type },
  });

  return { ok: true };
}

/** POST /api/photobooth/photos/:id/upload-complete — verifies the object landed in R2, then mints
 * the QR download token/URL (spec §39/§40). */
export async function completeUpload(request: IRequest, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  const photo = await env.DB
    .prepare("SELECT * FROM photobooth_photos WHERE id = ?")
    .bind(request.params.id)
    .first<PhotoRow>();

  if (!photo || photo.device_id !== auth.deviceId) {
    throw new StatusError(404, { error: "Photo not found." });
  }

  if (photo.status === "uploaded" && photo.download_token) {
    return {
      photoId: photo.id,
      downloadToken: photo.download_token,
      downloadUrl: `${env.GALLERY_BASE_URL}/${photo.download_token}`,
    };
  }

  const head = await env.ASSETS_BUCKET.head(photo.r2_key);
  if (!head) {
    throw new StatusError(409, { error: "Upload not found in storage yet — retry once the PUT has finished." });
  }

  const downloadToken = createDownloadToken();
  await env.DB
    .prepare("UPDATE photobooth_photos SET status = 'uploaded', uploaded_at = ?, download_token = ? WHERE id = ?")
    .bind(new Date().toISOString(), downloadToken, photo.id)
    .run();

  return {
    photoId: photo.id,
    downloadToken,
    downloadUrl: `${env.GALLERY_BASE_URL}/${downloadToken}`,
  };
}
