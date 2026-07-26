import { StatusError, type IRequest } from "itty-router";
import type { Env, PhotoRow } from "../types";
import { requireDeviceAuth } from "../lib/auth";
import { createDownloadToken, createId } from "../lib/ids";
import { createPresignedUploadUrl } from "../lib/r2";

const ALLOWED_CONTENT_TYPES: Record<string, string> = {
  "image/jpeg": "jpg",
  "image/png": "png",
};

/** POST /api/photobooth/photos — registers an upload and returns a presigned R2 PUT URL (spec §34,
 * §39). The device PUTs the file straight to R2, then calls upload-complete below. */
export async function createPhoto(request: IRequest, env: Env) {
  const auth = await requireDeviceAuth(request, env);
  if (!auth.ok) {
    throw new StatusError(auth.status, { error: auth.error });
  }

  const body = await request
    .json<{ eventId?: string; contentType?: string }>()
    .catch(() => ({}) as { eventId?: string; contentType?: string });
  const contentType = body.contentType ?? "image/jpeg";
  const extension = ALLOWED_CONTENT_TYPES[contentType];
  if (!extension) {
    throw new StatusError(400, { error: `Unsupported contentType '${contentType}'. Use image/jpeg or image/png.` });
  }

  const photoId = createId();
  const r2Key = `photos/${auth.deviceId}/${photoId}.${extension}`;
  const now = new Date();
  const retentionDays = Number(env.DEFAULT_PHOTO_RETENTION_DAYS);
  const expiresAt = retentionDays > 0 ? new Date(now.getTime() + retentionDays * 86_400_000).toISOString() : null;

  await env.DB
    .prepare(
      `INSERT INTO photobooth_photos (id, device_id, event_id, r2_key, content_type, status, created_at, expires_at)
       VALUES (?, ?, ?, ?, ?, 'pending-upload', ?, ?)`,
    )
    .bind(photoId, auth.deviceId, body.eventId ?? null, r2Key, contentType, now.toISOString(), expiresAt)
    .run();

  const uploadUrl = await createPresignedUploadUrl(env, env.R2_BUCKET_NAME, r2Key, contentType);

  return {
    photoId,
    uploadUrl,
    method: "PUT",
    requiredHeaders: { "content-type": contentType },
    expiresInSeconds: 900,
  };
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
