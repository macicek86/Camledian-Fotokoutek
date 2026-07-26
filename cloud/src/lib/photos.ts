import type { Env } from "../types";

/** Drops a photo's bytes from R2 and marks its DB row `expired` — keeping the row (rather than
 * deleting it) preserves it for the daily cleanup job's own audit/statistics purposes. Shared by the
 * scheduled cleanup (index.ts) and the admin gallery's manual delete action, so both paths agree on
 * what "removing a photo" means. */
export async function expirePhoto(env: Env, photo: { id: string; r2_key: string }): Promise<void> {
  await env.ASSETS_BUCKET.delete(photo.r2_key);
  await env.DB.prepare("UPDATE photobooth_photos SET status = 'expired' WHERE id = ?").bind(photo.id).run();
}
