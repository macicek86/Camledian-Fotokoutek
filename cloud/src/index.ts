import { AutoRouter, StatusError, cors, type IRequest } from "itty-router";
import type { Env } from "./types";
import { pairStart, pairStatus, pairConfirm } from "./routes/pairing";
import { getConfig } from "./routes/config";
import { listEvents, getEvent, getEventAssets, getAssetFile } from "./routes/events";
import { createPhoto, uploadPhoto, completeUpload } from "./routes/photos";
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

const { preflight, corsify } = cors({ allowMethods: ["GET", "POST", "PUT", "OPTIONS"] });

const isApiPath = (request: IRequest) => new URL(request.url).pathname.startsWith("/api/");

/** CORS belongs to the JSON device API only. Applying it globally also stamped
 * `access-control-allow-origin: *` onto every /admin/* page and the public gallery, which neither
 * needs — the Windows app isn't a browser and doesn't do preflights at all. */
const apiPreflight = (request: IRequest) => (isApiPath(request) ? preflight(request) : undefined);
const apiCorsify = (response: Response, request: IRequest) => (isApiPath(request) ? corsify(response, request) : response);

/** Baseline hardening for the session-authenticated admin console: no framing (the whole console is
 * one-click-destructive forms), no MIME sniffing, and a CSP tight enough that an escaped-HTML slip
 * somewhere couldn't load an external script. 'unsafe-inline' for styles is unavoidable while the
 * pages still use style="" attributes; scripts are external (/admin.js) and don't need it. */
const CSP = [
  "default-src 'self'",
  "img-src 'self' data:",
  "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
  "font-src https://fonts.gstatic.com",
  "script-src 'self'",
  "frame-ancestors 'none'",
  "form-action 'self'",
].join("; ");

const secureAdminHeaders = (response: Response, request: IRequest) => {
  if (!response || !new URL(request.url).pathname.startsWith("/admin")) return response;

  const hardened = new Response(response.body, response);
  hardened.headers.set("content-security-policy", CSP);
  hardened.headers.set("x-frame-options", "DENY");
  hardened.headers.set("x-content-type-options", "nosniff");
  hardened.headers.set("referrer-policy", "same-origin");
  return hardened;
};

const router = AutoRouter<IRequest, [Env, ExecutionContext]>({
  before: [apiPreflight],
  finally: [apiCorsify, secureAdminHeaders],
  catch: (error: unknown, request: IRequest) => {
    if (error instanceof StatusError) {
      return new Response(JSON.stringify(error.body ?? { error: error.message }), {
        status: error.status,
        headers: { "content-type": "application/json; charset=utf-8" },
      });
    }

    console.error(
      JSON.stringify({
        message: "unhandled error",
        path: new URL(request.url).pathname,
        method: request.method,
        error: error instanceof Error ? error.message : String(error),
        stack: error instanceof Error ? error.stack : undefined,
      }),
    );
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
  .put("/api/photobooth/photos/:id/upload", uploadPhoto)
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

  /** Nightly housekeeping (spec §43). Awaited rather than fire-and-forget: a rejection inside
   * ctx.waitUntil() would be swallowed, and this is the one code path with nobody watching it. */
  async scheduled(_controller: ScheduledController, env: Env, _ctx: ExecutionContext): Promise<void> {
    try {
      const photos = await cleanupExpiredPhotos(env);
      const sessions = await cleanupStaleRows(env);
      console.log(JSON.stringify({ message: "nightly cleanup finished", photos, ...sessions }));
    } catch (error) {
      console.error(
        JSON.stringify({
          message: "nightly cleanup failed",
          error: error instanceof Error ? error.message : String(error),
          stack: error instanceof Error ? error.stack : undefined,
        }),
      );
      throw error;
    }
  },
} satisfies ExportedHandler<Env>;

/** Kept under D1's 100-bound-parameter-per-statement cap, with room to spare for the two extra
 * parameters the SELECT binds. */
const CLEANUP_BATCH_SIZE = 90;

/** Drops the R2 bytes of retention-expired photos and marks their rows "expired", keeping the rows
 * for audit/statistics. Batched: the previous version selected every expired photo at once and then
 * did two binding calls per photo in a loop, which after a busy season would blow through the
 * per-invocation subrequest limit and — via waitUntil — fail silently. */
async function cleanupExpiredPhotos(env: Env): Promise<number> {
  const now = new Date().toISOString();
  let expired = 0;

  for (;;) {
    const { results } = await env.DB
      .prepare(
        `SELECT id, r2_key FROM photobooth_photos
         WHERE status = 'uploaded' AND expires_at IS NOT NULL AND expires_at < ?
         LIMIT ?`,
      )
      .bind(now, CLEANUP_BATCH_SIZE)
      .all<{ id: string; r2_key: string }>();

    if (results.length === 0) break;

    // One R2 call and one D1 call per batch instead of two per photo.
    await env.ASSETS_BUCKET.delete(results.map((photo) => photo.r2_key));
    await env.DB
      .prepare(`UPDATE photobooth_photos SET status = 'expired' WHERE id IN (${results.map(() => "?").join(", ")})`)
      .bind(...results.map((photo) => photo.id))
      .run();

    expired += results.length;
    if (results.length < CLEANUP_BATCH_SIZE) break;
  }

  return expired;
}

/** Neither of these tables was ever pruned: admin sessions accumulated one row per login forever,
 * and pairing codes kept their (now unusable) rows around indefinitely. */
async function cleanupStaleRows(env: Env): Promise<{ sessions: number; pairingCodes: number }> {
  const now = new Date();
  const [sessions, pairingCodes] = await env.DB.batch([
    env.DB.prepare("DELETE FROM photobooth_admin_sessions WHERE expires_at < ?").bind(now.toISOString()),
    env.DB
      .prepare("DELETE FROM photobooth_pairing_codes WHERE expires_at < ?")
      .bind(new Date(now.getTime() - 86_400_000).toISOString()),
  ]);

  return { sessions: sessions?.meta.changes ?? 0, pairingCodes: pairingCodes?.meta.changes ?? 0 };
}
