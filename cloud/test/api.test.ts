import { SELF, env } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import type { Env } from "../src/types";

// `env` is a test-only global from vitest-pool-workers giving direct binding access (see
// test/apply-migrations.ts) — used below to set up fixtures (a second admin, an already-"uploaded"
// photo row) as a shortcut where a test doesn't care about exercising the real upload flow itself
// (that flow has its own dedicated coverage in the "photo upload" describe block below).
const testEnv = env as unknown as Env;

// Must match the ADMIN_API_KEY binding configured in vitest.config.ts.
const ADMIN_KEY = "test-admin-key";

/** Ensures the "boss" admin exists (idempotent: /admin/setup 409s once an admin already exists,
 * which is fine here — we only care that "boss" exists by the time we try to log in) and returns
 * its session cookie, for tests that need a logged-in admin. */
async function loginAsBoss(): Promise<string> {
  await SELF.fetch("https://example.com/admin/setup", {
    method: "POST",
    headers: { "content-type": "application/json", "x-admin-key": ADMIN_KEY },
    body: JSON.stringify({ username: "boss", password: "correct-horse-battery" }),
  });

  const form = new URLSearchParams({ username: "boss", password: "correct-horse-battery" });
  const res = await SELF.fetch("https://example.com/admin/login", {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body: form.toString(),
    redirect: "manual",
  });
  const setCookie = res.headers.get("set-cookie") ?? "";
  return setCookie.split(";")[0] ?? "";
}

describe("landing + health", () => {
  it("serves the marketing landing page at /", async () => {
    const res = await SELF.fetch("https://example.com/");
    expect(res.status).toBe(200);
    expect(await res.text()).toContain("Camledian Fotokoutek");
  });

  it("responds on /api/health", async () => {
    const res = await SELF.fetch("https://example.com/api/health");
    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ ok: true });
  });
});

describe("device pairing (spec §36)", () => {
  it("rejects a malformed pairing code", async () => {
    const res = await SELF.fetch("https://example.com/api/photobooth/pair/start", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "not-a-code" }),
    });
    expect(res.status).toBe(400);
  });

  it("registers a pending code and reports it as pending", async () => {
    const start = await SELF.fetch("https://example.com/api/photobooth/pair/start", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "TEST-0001" }),
    });
    expect(start.status).toBe(200);

    const status = await SELF.fetch("https://example.com/api/photobooth/pair/status/TEST-0001");
    expect(await status.json()).toMatchObject({ status: "pending" });
  });

  it("refuses to confirm without the admin key", async () => {
    await SELF.fetch("https://example.com/api/photobooth/pair/start", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "TEST-0002" }),
    });

    const res = await SELF.fetch("https://example.com/api/photobooth/pair/confirm", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "TEST-0002" }),
    });
    expect(res.status).toBe(401);
  });

  it("confirms with the admin key and issues a usable device token", async () => {
    await SELF.fetch("https://example.com/api/photobooth/pair/start", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "TEST-0003" }),
    });

    const confirm = await SELF.fetch("https://example.com/api/photobooth/pair/confirm", {
      method: "POST",
      headers: { "content-type": "application/json", "x-admin-key": ADMIN_KEY },
      body: JSON.stringify({ code: "TEST-0003", deviceName: "Test Kiosk" }),
    });
    expect(confirm.status).toBe(200);

    const status = await SELF.fetch("https://example.com/api/photobooth/pair/status/TEST-0003");
    const body = await status.json<{ status: string; deviceToken: string }>();
    expect(body.status).toBe("confirmed");
    expect(body.deviceToken).toBeTruthy();

    // The freshly-issued token should authenticate against the device-only endpoints.
    const config = await SELF.fetch("https://example.com/api/photobooth/config", {
      headers: { authorization: `Bearer ${body.deviceToken}` },
    });
    expect(config.status).toBe(200);
  });
});

describe("device auth", () => {
  it("rejects requests with no Authorization header", async () => {
    const res = await SELF.fetch("https://example.com/api/photobooth/config");
    expect(res.status).toBe(401);
  });

  it("rejects an unknown bearer token", async () => {
    const res = await SELF.fetch("https://example.com/api/photobooth/config", {
      headers: { authorization: "Bearer not-a-real-token" },
    });
    expect(res.status).toBe(401);
  });
});

describe("gallery (spec §42)", () => {
  it("returns 404 with a noindex page for an unknown download token", async () => {
    const res = await SELF.fetch("https://example.com/foto/does-not-exist");
    expect(res.status).toBe(404);
    const html = await res.text();
    expect(html).toContain("noindex");
    expect(html).toContain("nenalezena");
  });
});

describe("admin bootstrap (spec §56 — standalone admin login)", () => {
  it("refuses /admin/setup without the bootstrap key", async () => {
    const res = await SELF.fetch("https://example.com/admin/setup", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: "someone", password: "hunter22222" }),
    });
    expect(res.status).toBe(401);
  });

  it("creates the first admin with the bootstrap key, then refuses a second bootstrap", async () => {
    const first = await SELF.fetch("https://example.com/admin/setup", {
      method: "POST",
      headers: { "content-type": "application/json", "x-admin-key": ADMIN_KEY },
      body: JSON.stringify({ username: "boss", password: "correct-horse-battery" }),
    });
    expect(first.status).toBe(200);

    const second = await SELF.fetch("https://example.com/admin/setup", {
      method: "POST",
      headers: { "content-type": "application/json", "x-admin-key": ADMIN_KEY },
      body: JSON.stringify({ username: "someone-else", password: "another-password" }),
    });
    expect(second.status).toBe(409);
  });
});

describe("admin UIs require a logged-in session", () => {
  it("redirects the old /admin/pair URL to /admin/devices", async () => {
    const res = await SELF.fetch("https://example.com/admin/pair", { redirect: "manual" });
    expect(res.status).toBe(303);
    expect(res.headers.get("location")).toContain("/admin/devices");
  });

  it("redirects /admin/devices to the login page when not logged in", async () => {
    const res = await SELF.fetch("https://example.com/admin/devices", { redirect: "manual" });
    expect(res.status).toBe(303);
    expect(res.headers.get("location")).toContain("/admin/login");
  });

  it("redirects /admin/gallery to the login page when not logged in", async () => {
    const res = await SELF.fetch("https://example.com/admin/gallery", { redirect: "manual" });
    expect(res.status).toBe(303);
    expect(res.headers.get("location")).toContain("/admin/login");
  });

  it("rejects a login with the wrong password", async () => {
    const form = new URLSearchParams({ username: "boss", password: "wrong-password" });
    const res = await SELF.fetch("https://example.com/admin/login", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: form.toString(),
      redirect: "manual",
    });
    expect(res.status).toBe(303);
    expect(res.headers.get("location")).toContain("/admin/login?error=1");
  });

  it("logs in and reaches /admin/stats and /admin/gallery with the session cookie", async () => {
    const cookie = await loginAsBoss();
    expect(cookie).toContain("admin_session=");

    const stats = await SELF.fetch("https://example.com/admin/stats", { headers: { cookie } });
    expect(stats.status).toBe(200);
    expect(await stats.text()).toContain("Poslední nahrané fotky");

    const gallery = await SELF.fetch("https://example.com/admin/gallery", { headers: { cookie } });
    expect(gallery.status).toBe(200);
    const html = await gallery.text();
    expect(html).toContain("Galerie");
    expect(html).toContain("Zatím žádné nahrané fotografie.");
  });

  it("logs out and revokes the session", async () => {
    const cookie = await loginAsBoss();

    const logoutRes = await SELF.fetch("https://example.com/admin/logout", {
      method: "POST",
      headers: { cookie },
      redirect: "manual",
    });
    expect(logoutRes.status).toBe(303);

    const afterLogout = await SELF.fetch("https://example.com/admin/stats", { headers: { cookie }, redirect: "manual" });
    expect(afterLogout.status).toBe(303);
    expect(afterLogout.headers.get("location")).toContain("/admin/login");
  });
});

describe("admin console: account passwords", () => {
  // Uses throwaway accounts rather than "boss" for the password-change assertions — other describe
  // blocks below share D1 storage across the whole test file (see loginAsBoss's own comment) and
  // rely on boss's fixed "correct-horse-battery" password to keep logging in.
  async function createAndLoginAs(username: string, password: string): Promise<string> {
    const bossCookie = await loginAsBoss();
    await SELF.fetch("https://example.com/admin/users", {
      method: "POST",
      headers: { cookie: bossCookie, "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ username, password }).toString(),
    });

    const res = await SELF.fetch("https://example.com/admin/login", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ username, password }).toString(),
      redirect: "manual",
    });
    return res.headers.get("set-cookie")?.split(";")[0] ?? "";
  }

  it("lets the logged-in admin change their own password", async () => {
    const cookie = await createAndLoginAs("pwtest", "first-password");

    const change = await SELF.fetch("https://example.com/admin/account/password", {
      method: "POST",
      headers: { cookie, "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ currentPassword: "first-password", newPassword: "second-password-1" }).toString(),
      redirect: "manual",
    });
    expect(change.status).toBe(303);
    expect(change.headers.get("location")).toContain("notice=password-changed");

    const oldLogin = await SELF.fetch("https://example.com/admin/login", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ username: "pwtest", password: "first-password" }).toString(),
      redirect: "manual",
    });
    expect(oldLogin.headers.get("location")).toContain("error=1");

    const newLogin = await SELF.fetch("https://example.com/admin/login", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ username: "pwtest", password: "second-password-1" }).toString(),
      redirect: "manual",
    });
    expect(newLogin.headers.get("location")).toContain("/admin/stats");
  });

  it("rejects the self password change when the current password is wrong", async () => {
    const cookie = await createAndLoginAs("pwtest2", "first-password");

    const change = await SELF.fetch("https://example.com/admin/account/password", {
      method: "POST",
      headers: { cookie, "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ currentPassword: "not-the-password", newPassword: "new-password-123" }).toString(),
    });
    expect(change.status).toBe(400);
  });

  it("lets an admin reset another admin's password, and blocks deleting your own account", async () => {
    const cookie = await loginAsBoss();

    await SELF.fetch("https://example.com/admin/users", {
      method: "POST",
      headers: { cookie, "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ username: "colleague", password: "first-password" }).toString(),
    });
    const colleague = await testEnv.DB.prepare("SELECT id FROM photobooth_admins WHERE username = ?").bind("colleague").first<{ id: string }>();
    expect(colleague).toBeTruthy();

    const reset = await SELF.fetch(`https://example.com/admin/users/${colleague!.id}/reset-password`, {
      method: "POST",
      headers: { cookie, "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ password: "second-password" }).toString(),
      redirect: "manual",
    });
    expect(reset.status).toBe(303);

    const login = await SELF.fetch("https://example.com/admin/login", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ username: "colleague", password: "second-password" }).toString(),
      redirect: "manual",
    });
    expect(login.headers.get("location")).toContain("/admin/stats");

    const boss = await testEnv.DB.prepare("SELECT id FROM photobooth_admins WHERE username = ?").bind("boss").first<{ id: string }>();
    const selfDelete = await SELF.fetch(`https://example.com/admin/users/${boss!.id}/delete`, {
      method: "POST",
      headers: { cookie },
    });
    expect(selfDelete.status).toBe(400);

    const deleteColleague = await SELF.fetch(`https://example.com/admin/users/${colleague!.id}/delete`, {
      method: "POST",
      headers: { cookie },
      redirect: "manual",
    });
    expect(deleteColleague.status).toBe(303);
    expect(deleteColleague.headers.get("location")).toContain("notice=user-deleted");
  });
});

describe("admin console: devices", () => {
  it("renames and revokes a paired device, blocking its token afterwards", async () => {
    await SELF.fetch("https://example.com/api/photobooth/pair/start", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "DEV1-0001" }),
    });
    const confirm = await SELF.fetch("https://example.com/api/photobooth/pair/confirm", {
      method: "POST",
      headers: { "content-type": "application/json", "x-admin-key": ADMIN_KEY },
      body: JSON.stringify({ code: "DEV1-0001", deviceName: "Kiosk A" }),
    });
    const { deviceId } = await confirm.json<{ deviceId: string }>();

    // The token comes from /pair/status, not from the confirm response — this test used to
    // destructure `deviceToken` off the confirm body, where it has never existed, so the
    // "revoked token stops working" assertion at the end was passing on `Bearer undefined`.
    const status = await SELF.fetch("https://example.com/api/photobooth/pair/status/DEV1-0001");
    const { deviceToken } = await status.json<{ deviceToken: string }>();
    expect(deviceToken).toBeTruthy();

    const cookie = await loginAsBoss();

    const rename = await SELF.fetch(`https://example.com/admin/devices/${deviceId}/rename`, {
      method: "POST",
      headers: { cookie, "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ name: "Kiosk u vchodu" }).toString(),
      redirect: "manual",
    });
    expect(rename.status).toBe(303);

    const devicesPage = await SELF.fetch("https://example.com/admin/devices", { headers: { cookie } });
    expect(await devicesPage.text()).toContain("Kiosk u vchodu");

    const revoke = await SELF.fetch(`https://example.com/admin/devices/${deviceId}/revoke`, {
      method: "POST",
      headers: { cookie },
      redirect: "manual",
    });
    expect(revoke.status).toBe(303);

    const configAfterRevoke = await SELF.fetch("https://example.com/api/photobooth/config", {
      headers: { authorization: `Bearer ${deviceToken}` },
    });
    expect(configAfterRevoke.status).toBe(401);
  });
});

describe("admin console: events", () => {
  it("creates an event from the browser form and toggles it inactive", async () => {
    const cookie = await loginAsBoss();

    const create = await SELF.fetch("https://example.com/admin/events", {
      method: "POST",
      headers: { cookie, "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ name: "Svatba Testovi", templateId: "digital-landscape", isActive: "1" }).toString(),
      redirect: "manual",
    });
    expect(create.status).toBe(303);

    const eventsPage = await SELF.fetch("https://example.com/admin/events", { headers: { cookie } });
    const html = await eventsPage.text();
    expect(html).toContain("Svatba Testovi");

    const event = await testEnv.DB.prepare("SELECT id, is_active FROM photobooth_events WHERE name = ?").bind("Svatba Testovi").first<{ id: string; is_active: number }>();
    expect(event?.is_active).toBe(1);

    const toggle = await SELF.fetch(`https://example.com/admin/events/${event!.id}/toggle`, {
      method: "POST",
      headers: { cookie },
      redirect: "manual",
    });
    expect(toggle.status).toBe(303);

    const afterToggle = await testEnv.DB.prepare("SELECT is_active FROM photobooth_events WHERE id = ?").bind(event!.id).first<{ is_active: number }>();
    expect(afterToggle?.is_active).toBe(0);
  });
});

describe("admin console: gallery photo deletion", () => {
  it("deletes an uploaded photo and removes it from the gallery", async () => {
    const cookie = await loginAsBoss();

    const deviceId = crypto.randomUUID();
    const photoId = crypto.randomUUID();
    const now = new Date().toISOString();
    await testEnv.DB
      .prepare("INSERT INTO photobooth_devices (id, name, token_hash, paired_at) VALUES (?, 'Fixture', 'unused', ?)")
      .bind(deviceId, now)
      .run();
    await testEnv.DB
      .prepare(
        `INSERT INTO photobooth_photos (id, device_id, event_id, r2_key, content_type, status, download_token, created_at, uploaded_at)
         VALUES (?, ?, NULL, 'photos/fixture.jpg', 'image/jpeg', 'uploaded', 'fixture-token-1234', ?, ?)`,
      )
      .bind(photoId, deviceId, now, now)
      .run();

    const galleryBefore = await SELF.fetch("https://example.com/admin/gallery", { headers: { cookie } });
    expect(await galleryBefore.text()).toContain("fixture-token-1234");

    const del = await SELF.fetch(`https://example.com/admin/gallery/${photoId}/delete`, {
      method: "POST",
      headers: { cookie },
      redirect: "manual",
    });
    expect(del.status).toBe(303);

    const status = await testEnv.DB.prepare("SELECT status FROM photobooth_photos WHERE id = ?").bind(photoId).first<{ status: string }>();
    expect(status?.status).toBe("expired");

    const galleryAfter = await SELF.fetch("https://example.com/admin/gallery", { headers: { cookie } });
    expect(await galleryAfter.text()).not.toContain("fixture-token-1234");
  });
});

// Placed last in the file: tests here leave real "uploaded" rows behind (unlike the other
// describe blocks, which clean up or use isolated fixtures), and this suite shares D1/R2 state
// sequentially across the whole file rather than isolating it per test — an earlier assertion
// like "gallery starts out empty" would otherwise see these leftover rows.
describe("photo upload (spec §34/§39)", () => {
  /** Pairs a fresh device and returns its Bearer token, for tests that need an authed device. */
  async function pairDevice(code: string): Promise<string> {
    await SELF.fetch("https://example.com/api/photobooth/pair/start", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code }),
    });
    await SELF.fetch("https://example.com/api/photobooth/pair/confirm", {
      method: "POST",
      headers: { "content-type": "application/json", "x-admin-key": ADMIN_KEY },
      body: JSON.stringify({ code, deviceName: "Upload Test Kiosk" }),
    });

    const status = await SELF.fetch(`https://example.com/api/photobooth/pair/status/${code}`);
    const { deviceToken } = await status.json<{ deviceToken: string }>();
    return deviceToken;
  }

  it("uploads straight through the Worker's R2 binding and completes without any presigned URL", async () => {
    const deviceToken = await pairDevice("UPLD-0001");

    const created = await SELF.fetch("https://example.com/api/photobooth/photos", {
      method: "POST",
      headers: { authorization: `Bearer ${deviceToken}`, "content-type": "application/json" },
      body: JSON.stringify({ contentType: "image/jpeg" }),
    });
    expect(created.status).toBe(200);
    const createdBody = await created.json<{ photoId: string; uploadUrl: string; method: string }>();
    expect(createdBody.method).toBe("PUT");
    // Relative, Worker-hosted path — not an external R2/S3 URL, so no separate R2 API credentials
    // are ever needed for this to work (that was the whole point of this route).
    expect(createdBody.uploadUrl).toBe(`/api/photobooth/photos/${createdBody.photoId}/upload`);

    const bytes = new Uint8Array([1, 2, 3, 4, 5]);
    const upload = await SELF.fetch(`https://example.com${createdBody.uploadUrl}`, {
      method: "PUT",
      headers: { authorization: `Bearer ${deviceToken}`, "content-type": "image/jpeg" },
      body: bytes,
    });
    expect(upload.status).toBe(200);

    const complete = await SELF.fetch(`https://example.com/api/photobooth/photos/${createdBody.photoId}/upload-complete`, {
      method: "POST",
      headers: { authorization: `Bearer ${deviceToken}` },
    });
    expect(complete.status).toBe(200);
    const completeBody = await complete.json<{ downloadToken: string; downloadUrl: string }>();
    expect(completeBody.downloadUrl).toContain(completeBody.downloadToken);

    const row = await testEnv.DB
      .prepare("SELECT status, r2_key FROM photobooth_photos WHERE id = ?")
      .bind(createdBody.photoId)
      .first<{ status: string; r2_key: string }>();
    expect(row?.status).toBe("uploaded");

    const stored = await testEnv.ASSETS_BUCKET.get(row!.r2_key);
    expect(stored).not.toBeNull();
    expect(new Uint8Array(await stored!.arrayBuffer())).toEqual(bytes);
  });

  it("refuses to upload against another device's photo", async () => {
    const ownerToken = await pairDevice("UPLD-0002");
    const otherToken = await pairDevice("UPLD-0003");

    const created = await SELF.fetch("https://example.com/api/photobooth/photos", {
      method: "POST",
      headers: { authorization: `Bearer ${ownerToken}`, "content-type": "application/json" },
      body: JSON.stringify({ contentType: "image/jpeg" }),
    });
    const { uploadUrl } = await created.json<{ uploadUrl: string }>();

    const upload = await SELF.fetch(`https://example.com${uploadUrl}`, {
      method: "PUT",
      headers: { authorization: `Bearer ${otherToken}`, "content-type": "image/jpeg" },
      body: new Uint8Array([9]),
    });
    expect(upload.status).toBe(404);
  });

  it("refuses a second upload against an already-uploaded photo", async () => {
    const deviceToken = await pairDevice("UPLD-0004");

    const created = await SELF.fetch("https://example.com/api/photobooth/photos", {
      method: "POST",
      headers: { authorization: `Bearer ${deviceToken}`, "content-type": "application/json" },
      body: JSON.stringify({ contentType: "image/jpeg" }),
    });
    const { photoId, uploadUrl } = await created.json<{ photoId: string; uploadUrl: string }>();

    const put = (body: Uint8Array) =>
      SELF.fetch(`https://example.com${uploadUrl}`, {
        method: "PUT",
        headers: { authorization: `Bearer ${deviceToken}`, "content-type": "image/jpeg" },
        body,
      });

    expect((await put(new Uint8Array([1, 1, 1]))).status).toBe(200);
    await SELF.fetch(`https://example.com/api/photobooth/photos/${photoId}/upload-complete`, {
      method: "POST",
      headers: { authorization: `Bearer ${deviceToken}` },
    });

    // The QR code is already out in the world at this point — swapping the bytes behind it would
    // silently change what a guest downloads.
    expect((await put(new Uint8Array([2, 2, 2]))).status).toBe(409);
  });

  it("rejects a contentType that only resolves through Object's prototype chain", async () => {
    const deviceToken = await pairDevice("UPLD-0005");

    const created = await SELF.fetch("https://example.com/api/photobooth/photos", {
      method: "POST",
      headers: { authorization: `Bearer ${deviceToken}`, "content-type": "application/json" },
      body: JSON.stringify({ contentType: "constructor" }),
    });
    expect(created.status).toBe(400);
  });
});

describe("hardening regressions", () => {
  it("no longer accepts the admin key as a ?key= query parameter", async () => {
    const viaQuery = await SELF.fetch(`https://example.com/api/photobooth/pair/confirm?key=${ADMIN_KEY}`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "QRY1-0001" }),
    });
    expect(viaQuery.status).toBe(401);
  });

  it("stops handing out the device token once the collection window has passed", async () => {
    await SELF.fetch("https://example.com/api/photobooth/pair/start", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "LATE-0001" }),
    });
    await SELF.fetch("https://example.com/api/photobooth/pair/confirm", {
      method: "POST",
      headers: { "content-type": "application/json", "x-admin-key": ADMIN_KEY },
      body: JSON.stringify({ code: "LATE-0001", deviceName: "Late Kiosk" }),
    });

    // Straight after confirmation the app can still collect it.
    const inWindow = await SELF.fetch("https://example.com/api/photobooth/pair/status/LATE-0001");
    expect((await inWindow.json<{ deviceToken: string | null }>()).deviceToken).toBeTruthy();

    // Wind the collection window back into the past, as it would be for someone coming back to a
    // pairing code they saw on the photobooth's screen earlier.
    await testEnv.DB
      .prepare("UPDATE photobooth_pairing_codes SET expires_at = ? WHERE code = 'LATE-0001'")
      .bind(new Date(Date.now() - 60_000).toISOString())
      .run();

    const tooLate = await SELF.fetch("https://example.com/api/photobooth/pair/status/LATE-0001");
    const body = await tooLate.json<{ status: string; deviceToken: string | null }>();
    expect(body.status).toBe("confirmed");
    expect(body.deviceToken).toBeNull();

    // ...and the token is wiped from the row, not merely withheld from the response.
    const row = await testEnv.DB
      .prepare("SELECT device_token FROM photobooth_pairing_codes WHERE code = 'LATE-0001'")
      .first<{ device_token: string | null }>();
    expect(row?.device_token).toBeNull();
  });

  it("refuses to serve a retention-expired photo's bytes before the cron has cleaned them up", async () => {
    const deviceId = crypto.randomUUID();
    const now = new Date().toISOString();
    const past = new Date(Date.now() - 60_000).toISOString();

    await testEnv.DB
      .prepare("INSERT INTO photobooth_devices (id, name, token_hash, paired_at) VALUES (?, 'Expiry Fixture', 'unused-expiry', ?)")
      .bind(deviceId, now)
      .run();
    await testEnv.ASSETS_BUCKET.put("photos/expired-fixture.jpg", new Uint8Array([7, 7, 7]));
    await testEnv.DB
      .prepare(
        `INSERT INTO photobooth_photos (id, device_id, event_id, r2_key, content_type, status, download_token, created_at, uploaded_at, expires_at)
         VALUES (?, ?, NULL, 'photos/expired-fixture.jpg', 'image/jpeg', 'uploaded', 'expired-token-1234', ?, ?, ?)`,
      )
      .bind(crypto.randomUUID(), deviceId, now, now, past)
      .run();

    // The page already refused; the file endpoint used to happily hand the bytes over anyway.
    const page = await SELF.fetch("https://example.com/foto/expired-token-1234");
    expect(page.status).toBe(410);

    const file = await SELF.fetch("https://example.com/foto/expired-token-1234/file");
    expect(file.status).toBe(410);
  });

  it("ignores an off-origin Referer when bouncing back from a gallery delete", async () => {
    const cookie = await loginAsBoss();

    const deviceId = crypto.randomUUID();
    const photoId = crypto.randomUUID();
    const now = new Date().toISOString();
    await testEnv.DB
      .prepare("INSERT INTO photobooth_devices (id, name, token_hash, paired_at) VALUES (?, 'Referer Fixture', 'unused-referer', ?)")
      .bind(deviceId, now)
      .run();
    await testEnv.DB
      .prepare(
        `INSERT INTO photobooth_photos (id, device_id, event_id, r2_key, content_type, status, download_token, created_at, uploaded_at)
         VALUES (?, ?, NULL, 'photos/referer-fixture.jpg', 'image/jpeg', 'uploaded', 'referer-token-1234', ?, ?)`,
      )
      .bind(photoId, deviceId, now, now)
      .run();

    const del = await SELF.fetch(`https://example.com/admin/gallery/${photoId}/delete`, {
      method: "POST",
      headers: { cookie, referer: "https://evil.example/admin/gallery" },
      redirect: "manual",
    });
    expect(del.status).toBe(303);
    expect(del.headers.get("location")).toBe("https://example.com/admin/gallery");
  });

  it("sends framing/CSP headers on admin pages", async () => {
    const cookie = await loginAsBoss();
    const res = await SELF.fetch("https://example.com/admin/stats", { headers: { cookie } });

    expect(res.headers.get("x-frame-options")).toBe("DENY");
    expect(res.headers.get("content-security-policy")).toContain("frame-ancestors 'none'");
    // CORS is for the JSON device API only — it has no business on a cookie-authenticated page.
    expect(res.headers.get("access-control-allow-origin")).toBeNull();
  });
});
