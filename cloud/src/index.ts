import { AutoRouter, StatusError, cors, type IRequest } from "itty-router";
import type { Env } from "./types";
import { pairStart, pairStatus, pairConfirm } from "./routes/pairing";
import { getConfig } from "./routes/config";
import { listEvents, getEvent, getEventAssets, getAssetFile } from "./routes/events";
import { createPhoto, completeUpload } from "./routes/photos";
import { heartbeat } from "./routes/heartbeat";
import { galleryPage, galleryFile } from "./routes/gallery";
import { adminPairPage, adminPairConfirmForm } from "./routes/adminPairUi";
import { adminStatsPage } from "./routes/adminStats";
import { landingPage } from "./routes/landing";

const { preflight, corsify } = cors();

const router = AutoRouter<IRequest, [Env, ExecutionContext]>({
  before: [preflight],
  finally: [corsify],
  catch: (error) => {
    if (error instanceof StatusError) {
      return new Response(JSON.stringify(error.body ?? { error: error.message }), {
        status: error.status,
        headers: { "content-type": "application/json; charset=utf-8" },
      });
    }

    console.error(error);
    return new Response(JSON.stringify({ error: "Internal server error." }), {
      status: 500,
      headers: { "content-type": "application/json; charset=utf-8" },
    });
  },
});

router
  // Public marketing landing page for the bare fotokoutek.camledian.art domain.
  .get("/", landingPage)
  .get("/api/health", () => ({ name: "Camledian Photobooth API", ok: true }))

  // Device pairing (spec §36)
  .post("/api/photobooth/pair/start", pairStart)
  .get("/api/photobooth/pair/status/:code", pairStatus)
  .post("/api/photobooth/pair/confirm", pairConfirm)

  // Config / events / asset manifest (spec §32/§37)
  .get("/api/photobooth/config", getConfig)
  .get("/api/photobooth/events", listEvents)
  .get("/api/photobooth/events/:id", getEvent)
  .get("/api/photobooth/events/:id/assets", getEventAssets)
  .get("/assets/:id", getAssetFile)

  // Photo upload (spec §34/§39)
  .post("/api/photobooth/photos", createPhoto)
  .post("/api/photobooth/photos/:id/upload-complete", completeUpload)

  // Heartbeat (spec §32/§44)
  .post("/api/photobooth/heartbeat", heartbeat)

  // Public QR gallery (spec §42)
  .get("/foto/:token", galleryPage)
  .get("/foto/:token/file", galleryFile)

  // Minimal dev admin UI (spec §31/§36/§44)
  .get("/admin/pair", adminPairPage)
  .post("/admin/pair/confirm", adminPairConfirmForm)
  .get("/admin/stats", adminStatsPage);

export default {
  fetch: router.fetch,

  /** Daily cleanup of expired photos (spec §43): drop the R2 bytes, keep the DB row (marked
   * "expired") for audit/statistics purposes. */
  async scheduled(_event: ScheduledEvent, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil(cleanupExpiredPhotos(env));
  },
};

async function cleanupExpiredPhotos(env: Env): Promise<void> {
  const now = new Date().toISOString();
  const { results } = await env.DB
    .prepare("SELECT id, r2_key FROM photobooth_photos WHERE status = 'uploaded' AND expires_at IS NOT NULL AND expires_at < ?")
    .bind(now)
    .all<{ id: string; r2_key: string }>();

  for (const photo of results) {
    await env.ASSETS_BUCKET.delete(photo.r2_key);
    await env.DB.prepare("UPDATE photobooth_photos SET status = 'expired' WHERE id = ?").bind(photo.id).run();
  }

  if (results.length > 0) {
    console.log(`Cleaned up ${results.length} expired photo(s).`);
  }
}
