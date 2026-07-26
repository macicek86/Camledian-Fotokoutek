import { SELF } from "cloudflare:test";
import { describe, expect, it } from "vitest";

// Must match the ADMIN_API_KEY binding configured in vitest.config.ts.
const ADMIN_KEY = "test-admin-key";

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
  async function loginCookie(): Promise<string> {
    // Idempotent: /admin/setup 409s once an admin already exists, which is fine here — we only
    // care that "boss" exists by the time we try to log in with it.
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

  it("redirects /admin/pair to the login page when not logged in", async () => {
    const res = await SELF.fetch("https://example.com/admin/pair", { redirect: "manual" });
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
    const cookie = await loginCookie();
    expect(cookie).toContain("admin_session=");

    const stats = await SELF.fetch("https://example.com/admin/stats", { headers: { cookie } });
    expect(stats.status).toBe(200);
    expect(await stats.text()).toContain("Statistiky fotokoutku");

    const gallery = await SELF.fetch("https://example.com/admin/gallery", { headers: { cookie } });
    expect(gallery.status).toBe(200);
    const html = await gallery.text();
    expect(html).toContain("Přehled fotografií");
    expect(html).toContain("Zatím žádné nahrané fotografie.");
  });

  it("logs out and revokes the session", async () => {
    const cookie = await loginCookie();

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
