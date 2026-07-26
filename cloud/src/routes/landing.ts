/**
 * GET / — public marketing landing page for fotokoutek.camledian.art (not part of the device API;
 * just "who we are / what this photobooth offers" for visitors who land on the bare domain).
 * Pricing is a placeholder — edit CENIK below once real packages are decided. Logo/favicon files
 * live in cloud/public/ (served directly by the Workers static-assets binding, see wrangler.toml).
 *
 * Light theme by design: the logo's navy/gold artwork was made for a light background, so the page
 * uses one instead of trying to carve out a light patch behind it on a dark page.
 */
export function landingPage(): Response {
  const html = `<!doctype html>
<html lang="cs">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Camledian Fotokoutek</title>
<meta name="description" content="Camledian Fotokoutek — moderní fotokoutek na svatby, firemní akce a oslavy, s green screen a AI úpravou pozadí.">
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<style>
  :root {
    color-scheme: light;
    --bg: #f7f2e7;
    --panel: #ffffff;
    --border: #e6ddc6;
    --navy: #0d1b2e;
    --gold: #b8912a;
    --gold-bright: #d4af37;
    --text-muted: #5b6472;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; background: var(--bg); color: var(--navy);
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    line-height: 1.5;
  }
  header {
    padding: 64px 24px 56px; text-align: center;
    background: radial-gradient(circle at 50% 0%, #fffdf6 0%, var(--bg) 75%);
    border-bottom: 1px solid var(--border);
  }
  header img.badge { display: block; margin: 0 auto; width: 260px; max-width: 65vw; height: auto; }
  header h1 {
    font-size: clamp(2rem, 5vw, 3.2rem); margin: 24px 0 16px; color: var(--navy); letter-spacing: 0.02em;
  }
  header p { color: var(--text-muted); font-size: 1.2rem; max-width: 560px; margin: 0 auto; }
  main { max-width: 960px; margin: 0 auto; padding: 0 24px 96px; }
  section { margin-top: 64px; }
  h2 { font-size: 1.6rem; border-left: 4px solid var(--gold); padding-left: 16px; color: var(--navy); }
  .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 20px; margin-top: 24px; }
  .card {
    background: var(--panel); border: 1px solid var(--border); border-radius: 16px; padding: 24px;
    box-shadow: 0 4px 16px rgba(13,27,46,0.06);
  }
  .card h3 { margin-top: 0; color: var(--gold); }
  table.pricing { width: 100%; border-collapse: collapse; margin-top: 24px; background: var(--panel); border-radius: 12px; overflow: hidden; }
  table.pricing th, table.pricing td { padding: 14px 16px; border-bottom: 1px solid var(--border); text-align: left; }
  table.pricing th { color: var(--text-muted); font-weight: 600; }
  .price { color: var(--gold); font-weight: 700; font-size: 1.1rem; }
  footer { text-align: center; padding: 32px 24px; color: var(--text-muted); font-size: 0.9rem; }
  a.cta {
    display: inline-block; margin-top: 32px; background: var(--navy); color: #fff; text-decoration: none;
    font-weight: 700; padding: 16px 36px; border-radius: 12px;
  }
  a.cta:hover { background: #16283f; }
  footer a { color: var(--text-muted); }
</style>
</head>
<body>
  <header>
    <img class="badge" src="/logo-full.png" alt="Camledian Fotokoutek">
    <h1>Camledian Fotokoutek</h1>
    <p>Moderní fotokoutek se zeleným plátnem, AI úpravou pozadí a okamžitým sdílením fotek přes QR kód —
       na svatby, firemní akce i oslavy.</p>
    <a class="cta" href="mailto:info@camledian.art">Poptat fotokoutek</a>
  </header>
  <main>
    <section>
      <h2>Co nabízíme</h2>
      <div class="cards">
        <div class="card"><h3>Vlastní pozadí</h3><p>Zelené plátno + libovolné digitální pozadí podle tématu vaší akce.</p></div>
        <div class="card"><h3>AI a hybridní režim</h3><p>Čisté ořezy i bez zeleného plátna, včetně vlasů a rukou.</p></div>
        <div class="card"><h3>Okamžité sdílení</h3><p>Fotka je hned po vyfocení ke stažení přes QR kód na mobil.</p></div>
        <div class="card"><h3>Tisk na místě</h3><p>Volitelný okamžitý tisk fotografií přímo na akci.</p></div>
      </div>
    </section>
    <section>
      <h2>Ceník</h2>
      <p style="color: var(--text-muted)">Orientační ceny — přesnou nabídku připravíme na míru podle délky akce a počtu výtisků.</p>
      <table class="pricing">
        <thead><tr><th>Balíček</th><th>Délka</th><th>Cena</th></tr></thead>
        <tbody>
          <tr><td>Základní</td><td>3 hodiny</td><td class="price">od 4 900 Kč</td></tr>
          <tr><td>Standard</td><td>5 hodin</td><td class="price">od 6 900 Kč</td></tr>
          <tr><td>Celý den</td><td>8+ hodin</td><td class="price">na dotaz</td></tr>
        </tbody>
      </table>
    </section>
  </main>
  <footer>Camledian Fotokoutek &middot; <a href="mailto:info@camledian.art">info@camledian.art</a></footer>
</body>
</html>`;

  return new Response(html, { headers: { "content-type": "text/html; charset=utf-8" } });
}
