import type { IRequest } from "itty-router";
import type { Env, PhotoRow } from "../types";

/**
 * The page a guest lands on after scanning the QR code — for most of them the only Camledian web
 * page they ever see, and almost always on a phone. Hence: light theme matching the landing page
 * (the logo's navy "FOTOKOUTEK" wordmark is invisible on a dark background), one card, one
 * full-width download button, and nothing else competing for the thumb.
 */
function renderPage(body: string): string {
  return `<!doctype html>
<html lang="cs">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="robots" content="noindex, nofollow">
<meta name="theme-color" content="#f7f2e7">
<title>Camledian Fotokoutek — vaše fotografie</title>
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<style>
  /* Same palette as the landing page (routes/landing.ts) — one brand, two pages. */
  :root {
    color-scheme: light;
    --bg: #f7f2e7; --panel: #ffffff; --border: #e6ddc6;
    --navy: #0d1b2e; --gold: #b8912a; --text-muted: #5b6472;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; min-height: 100vh; min-height: 100dvh;
    display: flex; flex-direction: column; align-items: center; justify-content: center;
    background: radial-gradient(circle at 50% 0%, #fffdf6 0%, var(--bg) 70%) no-repeat;
    background-color: var(--bg);
    color: var(--navy); font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    line-height: 1.5; text-align: center;
    padding: 24px 16px max(24px, env(safe-area-inset-bottom));
  }
  main {
    width: 100%; max-width: 480px; background: var(--panel); border: 1px solid var(--border);
    border-radius: 20px; padding: 24px 20px; box-shadow: 0 6px 28px rgba(13,27,46,0.08);
  }
  /* Same logo as the kiosk's idle screen (assets/branding/logo-full.png), re-encoded for mobile:
     transparent margin cropped, 420px wide, 128-color palette — 583 kB down to 52 kB. */
  img.badge { display: block; margin: 0 auto 20px; width: 160px; max-width: 45vw; height: auto; }
  /* The photo takes whatever vertical space the fixed chunks (logo, headings, button, footer ≈ 520 px)
     leave over, so the STÁHNOUT button stays above the fold even for a portrait shot on a small phone.
     The 180px floor stops it collapsing to a sliver on a very short screen — there we do scroll. */
  img.photo {
    display: block; width: auto; max-width: 100%; margin: 0 auto;
    max-height: max(180px, min(58vh, calc(100dvh - 520px)));
    border-radius: 12px; background: var(--bg);
  }
  /* Short screens (small Androids, Safari with the URL bar showing): give the photo its pixels back
     by shrinking the branding rather than pushing the button off the bottom. */
  @media (max-height: 700px) {
    img.badge { width: 110px; margin-bottom: 14px; }
    h1 { font-size: 1.15rem; margin-top: 14px; }
    a.button { padding: 15px 24px; }
    img.photo { max-height: max(140px, calc(100dvh - 470px)); }
  }
  h1 { font-size: 1.35rem; margin: 20px 0 6px; letter-spacing: 0.01em; }
  p { color: var(--text-muted); margin: 0; }
  a.button {
    display: block; margin-top: 20px; background: var(--navy); color: #fff; text-decoration: none;
    font-weight: 700; font-size: 1.1rem; letter-spacing: 0.04em; padding: 18px 24px; border-radius: 14px;
    border-bottom: 3px solid var(--gold);
  }
  a.button:active { background: #16283f; }
  footer { margin-top: 20px; font-size: 0.85rem; color: var(--text-muted); }
  footer a { color: var(--gold); text-decoration: none; }
</style>
</head>
<body>
<main>
<img class="badge" src="/logo-web.png" width="420" height="453" alt="Camledian Fotokoutek">
${body}
</main>
<footer>Fotil vás <a href="/">Camledian Fotokoutek</a></footer>
</body>
</html>`;
}

/** Guests have no idea the link dies after the retention period, so say it on the page. Silent on a
 * malformed date rather than printing "Invalid Date" at them. */
function expiryNote(expiresAt: string | null): string {
  if (!expiresAt) return "";
  const date = new Date(expiresAt);
  if (Number.isNaN(date.getTime())) return "";
  const formatted = new Intl.DateTimeFormat("cs-CZ", { dateStyle: "long", timeZone: "Europe/Prague" }).format(date);
  return ` Odkaz je platný do ${formatted}.`;
}

/** These pages are per-guest and short-lived (a token stops working the moment retention runs out),
 * and Cloudflare caches 404s at the edge by default — which is how a stale "not found" can outlive
 * both the fix and the photo. no-store keeps every visit answered by the Worker. */
function htmlResponse(html: string, status = 200): Response {
  return new Response(html, {
    status,
    headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" },
  });
}

/** GET /foto/:token — spec §42: responsive mobile page with a download button, noindex. */
export async function galleryPage(request: IRequest, env: Env) {
  const token = request.params.token;
  const photo = await env.DB
    .prepare("SELECT * FROM photobooth_photos WHERE download_token = ?")
    .bind(token)
    .first<PhotoRow>();

  if (!photo || photo.status !== "uploaded") {
    return htmlResponse(
      renderPage("<h1>Fotografie nenalezena</h1><p>Odkaz je neplatný nebo fotografie ještě není nahraná.</p>"),
      404,
    );
  }

  if (photo.expires_at && new Date(photo.expires_at).getTime() < Date.now()) {
    return htmlResponse(
      renderPage("<h1>Odkaz vypršel</h1><p>Tato fotografie už byla po uplynutí doby uchování odstraněna.</p>"),
      410,
    );
  }

  // The token from the row, not from the URL: it is what the lookup matched, and it can never carry
  // anything that would break out of the attribute it is interpolated into.
  const fileUrl = `/foto/${encodeURIComponent(photo.download_token ?? "")}/file`;
  const html = renderPage(`
    <img class="photo" src="${fileUrl}" alt="Vaše fotografie z Camledian Photobooth">
    <h1>Vaše fotografie je připravena</h1>
    <p>Stáhněte si ji do telefonu nebo počítače.${expiryNote(photo.expires_at)}</p>
    <a class="button" href="${fileUrl}?download=1">STÁHNOUT</a>
  `);

  return htmlResponse(html);
}

/** "Svatba Novákovi 2026" -> "svatba-novakovi-2026". Diacritics are stripped rather than percent-
 * encoded so the Content-Disposition filename stays plain ASCII and survives every phone's download
 * manager. Returns "" when nothing usable is left (e.g. an emoji-only event name). */
function slugify(value: string): string {
  return value
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 40)
    .replace(/-+$/, "");
}

/** Capture time in Prague local time as "2026-07-27-1435" — that is the moment the guest remembers,
 * not the UTC instant. sv-SE is the shortest way to an ISO-shaped date+time out of Intl. */
function captureStamp(createdAt: string): string {
  const date = new Date(createdAt);
  if (Number.isNaN(date.getTime())) return "";
  const formatted = new Intl.DateTimeFormat("sv-SE", {
    dateStyle: "short",
    timeStyle: "short",
    timeZone: "Europe/Prague",
  }).format(date);
  return formatted.replace(" ", "-").replace(":", "");
}

/** camledian-fotokoutek-2026-07-27-1435-svatba-novakovi.jpg — guests download dozens of these into
 * one folder, so the name has to say when it was taken and at which event. Both parts are optional:
 * a photo with no event (or an unparseable date) just drops that segment. */
function downloadFilename(photo: PhotoRow, eventName: string | null, extension: string): string {
  const parts = ["camledian-fotokoutek", captureStamp(photo.created_at), eventName ? slugify(eventName) : ""];
  return `${parts.filter(Boolean).join("-")}.${extension}`;
}

/** GET /foto/:token/file — the actual image bytes, streamed straight from R2. */
export async function galleryFile(request: IRequest, env: Env) {
  const photo = await env.DB
    .prepare(
      `SELECT p.*, e.name AS event_name
         FROM photobooth_photos p
         LEFT JOIN photobooth_events e ON e.id = p.event_id
        WHERE p.download_token = ?`,
    )
    .bind(request.params.token)
    .first<PhotoRow & { event_name: string | null }>();

  if (!photo || photo.status !== "uploaded") {
    return new Response("Not found", { status: 404 });
  }

  // Same retention check galleryPage does. Without it the bytes stayed downloadable through this
  // direct URL until the nightly cron got round to deleting them — up to ~24h past the retention
  // deadline the page itself already refuses to serve.
  if (photo.expires_at && new Date(photo.expires_at).getTime() < Date.now()) {
    return new Response("Gone", { status: 410 });
  }

  const object = await env.ASSETS_BUCKET.get(photo.r2_key);
  if (!object) {
    return new Response("Not found", { status: 404 });
  }

  const url = new URL(request.url);
  const contentType = object.httpMetadata?.contentType ?? photo.content_type;
  const headers = new Headers({
    "content-type": contentType,
    "cache-control": "private, max-age=3600",
  });
  if (url.searchParams.has("download")) {
    const extension = contentType === "image/png" ? "png" : "jpg";
    const filename = downloadFilename(photo, photo.event_name, extension);
    headers.set("content-disposition", `attachment; filename="${filename}"`);
  }

  return new Response(object.body, { headers });
}
