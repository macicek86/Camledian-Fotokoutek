import { AutoRouter, StatusError, cors, type IRequest } from "itty-router";
import type { Env } from "./types";
import { expirePhoto } from "./lib/photos";
import { pairStart, pairStatus, pairConfirm } from "./routes/pairing";
import { getConfig } from "./routes/config";
import { listEvents, getEvent, getEventAssets, getAssetFile } from "./routes/events";
import { createPhoto, completeUpload } from "./routes/photos";
import { heartbeat } from "./routes/heartbeat";
import { galleryPage, galleryFile } from "./routes/gallery";
import { adminDevicesPage, adminPairRedirect, adminPairConfirmForm, renameDevice, revokeDevice } from "./routes/adminDevices";
import { adminEventsPage, createEvent, updateEvent, toggleEvent } from "./routes/adminEvents";
import { adminStatsPage } from "./routes/adminStats";
import { adminGalleryPage, deleteGalleryPhoto } from "./routes/adminGallery";
import {
  loginPage,
  loginSubmit,
  logout,
  setupAdmin,
  usersPage,
  createUser,
  changeOwnPassword,
  resetUserPassword,
  deleteUser,
} from "./routes/adminAuth";
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

  // Standalone admin login (own accounts + sessions; ADMIN_API_KEY is bootstrap/server-to-server only)
  .get("/admin/login", loginPage)
  .post("/admin/login", loginSubmit)
  .post("/admin/logout", logout)
  .post("/admin/setup", setupAdmin)
  .get("/admin/users", usersPage)
  .post("/admin/users", createUser)
  .post("/admin/account/password", changeOwnPassword)
  .post("/admin/users/:id/reset-password", resetUserPassword)
  .post("/admin/users/:id/delete", deleteUser)

  // Admin console (spec §31/§36/§44)
  .get("/admin", (request: Request) => Response.redirect(new URL("/admin/stats", request.url).toString(), 303))
  .get("/admin/pair", adminPairRedirect)
  .post("/admin/pair/confirm", adminPairConfirmForm)
  .get("/admin/stats", adminStatsPage)
  .get("/admin/gallery", adminGalleryPage)
  .post("/admin/gallery/:id/delete", deleteGalleryPhoto)
  .get("/admin/devices", adminDevicesPage)
  .post("/admin/devices/:id/rename", renameDevice)
  .post("/admin/devices/:id/revoke", revokeDevice)
  .get("/admin/events", adminEventsPage)
  .post("/admin/events", createEvent)
  .post("/admin/events/:id", updateEvent)
  .post("/admin/events/:id/toggle", toggleEvent);

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
    await expirePhoto(env, photo);
  }

  if (results.length > 0) {
    console.log(`Cleaned up ${results.length} expired photo(s).`);
  }
}
