import type { IRequest } from "itty-router";
import type { Env, PhotoRow } from "../types";

function renderPage(body: string): string {
  return `<!doctype html>
<html lang="cs">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title>Camledian Fotokoutek — vaše fotografie</title>
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<style>
  :root { color-scheme: dark; --navy: #0d1b2e; --gold: #d4af37; --text-muted: #9fb0c3; }
  * { box-sizing: border-box; }
  body {
    margin: 0; min-height: 100vh; display: flex; flex-direction: column; align-items: center; justify-content: center;
    background: var(--navy); color: #f5f5f5; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    padding: 24px; text-align: center;
  }
  img.photo { max-width: min(90vw, 640px); max-height: 70vh; border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.4); }
  img.badge { width: 56px; height: 56px; margin-bottom: 8px; }
  h1 { font-size: 1.4rem; margin: 24px 0 8px; }
  p { color: var(--text-muted); margin: 0 0 24px; }
  a.button {
    display: inline-block; background: var(--gold); color: var(--navy); text-decoration: none; font-weight: 700;
    padding: 16px 40px; border-radius: 12px; font-size: 1.1rem; margin-top: 8px;
  }
</style>
</head>
<body>
<img class="badge" src="/favicon-32.png" alt="">
${body}
</body>
</html>`;
}

/** GET /foto/:token — spec §42: responsive mobile page with a download button, noindex. */
export async function galleryPage(request: IRequest, env: Env) {
  const token = request.params.token;
  const photo = await env.DB
    .prepare("SELECT * FROM photobooth_photos WHERE download_token = ?")
    .bind(token)
    .first<PhotoRow>();

  if (!photo || photo.status !== "uploaded") {
    return new Response(
      renderPage("<h1>Fotografie nenalezena</h1><p>Odkaz je neplatný nebo fotografie ještě není nahraná.</p>"),
      { status: 404, headers: { "content-type": "text/html; charset=utf-8" } },
    );
  }

  if (photo.expires_at && new Date(photo.expires_at).getTime() < Date.now()) {
    return new Response(
      renderPage("<h1>Odkaz vypršel</h1><p>Tato fotografie už byla po uplynutí doby uchování odstraněna.</p>"),
      { status: 410, headers: { "content-type": "text/html; charset=utf-8" } },
    );
  }

  const fileUrl = `/foto/${token}/file`;
  const html = renderPage(`
    <img class="photo" src="${fileUrl}" alt="Vaše fotografie z Camledian Photobooth">
    <h1>Vaše fotografie je připravena</h1>
    <p>Stáhněte si ji do telefonu nebo počítače.</p>
    <a class="button" href="${fileUrl}?download=1">STÁHNOUT</a>
  `);

  return new Response(html, { headers: { "content-type": "text/html; charset=utf-8" } });
}

/** GET /foto/:token/file — the actual image bytes, streamed straight from R2. */
export async function galleryFile(request: IRequest, env: Env) {
  const photo = await env.DB
    .prepare("SELECT * FROM photobooth_photos WHERE download_token = ?")
    .bind(request.params.token)
    .first<PhotoRow>();

  if (!photo || photo.status !== "uploaded") {
    return new Response("Not found", { status: 404 });
  }

  const object = await env.ASSETS_BUCKET.get(photo.r2_key);
  if (!object) {
    return new Response("Not found", { status: 404 });
  }

  const url = new URL(request.url);
  const headers = new Headers({
    "content-type": object.httpMetadata?.contentType ?? photo.content_type,
    "cache-control": "private, max-age=3600",
  });
  if (url.searchParams.has("download")) {
    headers.set("content-disposition", `attachment; filename="camledian-photobooth-${photo.id}.jpg"`);
  }

  return new Response(object.body, { headers });
}
