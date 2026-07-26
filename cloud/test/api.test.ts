import { SELF, env } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import type { Env } from "../src/types";

// `env` is a test-only global from vitest-pool-workers giving direct binding access (see
// test/apply-migrations.ts) — used below to set up fixtures (a second admin, an uploaded photo)
// that would otherwise require a full R2 presigned-upload round trip this test env isn't wired for.
const testEnv = env as unknown as Env;

// Must match the ADMIN_API_KEY binding configured in vitest.config.ts.
const ADMIN_KEY = "test-admin-key";

/** Ensures the "boss" admin exists (idempotent: /admin/setup 409s once an admin already exists,
 * which is fine here — we only care that "boss" exists by the time we try to log in) and returns
 * its session cookie, for tests that need a logged-in admin. */
async function loginAsBoss(): Promise<string> {
  await SELF.fetch(`https://example.com/admin/setup?key=${ADMIN_KEY}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
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

    const confirm = await SELF.fetch(`https://example.com/api/photobooth/pair/confirm?key=${ADMIN_KEY}`, {
      method: "POST",
      headers: { "content-type": "application/json" },
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
    const first = await SELF.fetch(`https://example.com/admin/setup?key=${ADMIN_KEY}`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: "boss", password: "correct-horse-battery" }),
    });
    expect(first.status).toBe(200);

    const second = await SELF.fetch(`https://example.com/admin/setup?key=${ADMIN_KEY}`, {
      method: "POST",
      headers: { "content-type": "application/json" },
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
    const confirm = await SELF.fetch(`https://example.com/api/photobooth/pair/confirm?key=${ADMIN_KEY}`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: "DEV1-0001", deviceName: "Kiosk A" }),
    });
    const { deviceId, deviceToken } = await confirm.json<{ deviceId: string; deviceToken: string }>();

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
