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

describe("admin UIs are key-protected", () => {
  it("blocks /admin/pair without the key", async () => {
    const res = await SELF.fetch("https://example.com/admin/pair");
    expect(res.status).toBe(401);
  });

  it("blocks /admin/stats without the key", async () => {
    const res = await SELF.fetch("https://example.com/admin/stats");
    expect(res.status).toBe(401);
  });

  it("allows /admin/stats with the correct key", async () => {
    const res = await SELF.fetch(`https://example.com/admin/stats?key=${ADMIN_KEY}`);
    expect(res.status).toBe(200);
    expect(await res.text()).toContain("Statistiky fotokoutku");
  });
});
