import { StatusError } from "itty-router";
import type { AdminRow, Env } from "../types";
import { clearAdminSessionCookie, createAdminSession, findAdminByUsername, requireAdminKey, requireAdminSession } from "../lib/auth";
import { createId, sha256Hex } from "../lib/ids";
import { hashPassword, verifyPassword } from "../lib/password";

function loginPageHtml(error?: string): string {
  return `<!doctype html>
<html lang="cs"><head><meta charset="utf-8"><title>Přihlášení — administrace</title>
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<style>
  body { font-family: system-ui, sans-serif; background: #0d1b2e; color: #f5f5f5; padding: 32px;
         display: flex; align-items: center; justify-content: center; min-height: 80vh; }
  form { background: #16283f; border-radius: 12px; padding: 32px; width: 100%; max-width: 320px; }
  h1 { font-size: 1.2rem; margin: 0 0 20px; }
  label { display: block; font-size: 0.9rem; color: #9fb0c3; margin: 12px 0 4px; }
  input { width: 100%; padding: 8px 10px; font-size: 1rem; border-radius: 6px; border: 1px solid #223650;
          background: #0d1b2e; color: #f5f5f5; box-sizing: border-box; }
  button { margin-top: 20px; width: 100%; padding: 10px; font-size: 1rem; font-weight: 700; border: none;
           border-radius: 6px; background: #d4af37; color: #0d1b2e; cursor: pointer; }
  .error { color: #e0a458; font-size: 0.9rem; margin: 0 0 12px; }
</style></head>
<body>
  <form method="post" action="/admin/login">
    <h1>Administrace Camledian Fotokoutek</h1>
    ${error ? `<p class="error">${error}</p>` : ""}
    <label for="username">Uživatelské jméno</label>
    <input type="text" id="username" name="username" autocomplete="username" required autofocus>
    <label for="password">Heslo</label>
    <input type="password" id="password" name="password" autocomplete="current-password" required>
    <button type="submit">Přihlásit se</button>
  </form>
</body></html>`;
}

/** GET /admin/login — plain login form; the human-facing counterpart to requireAdminKey (spec §56
 * eventually wants this wired to Camledian's own administration, but per the owner that's not
 * happening yet — this needs to stand on its own). */
export async function loginPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (session.ok) {
    return Response.redirect(new URL("/admin/pair", request.url).toString(), 303);
  }

  const url = new URL(request.url);
  const error = url.searchParams.get("error") === "1" ? "Nesprávné jméno nebo heslo." : undefined;
  return new Response(loginPageHtml(error), { headers: { "content-type": "text/html; charset=utf-8" } });
}

/** POST /admin/login — verifies credentials against photobooth_admins and, on success, sets an
 * HttpOnly session cookie (see lib/auth.ts createAdminSession). Never puts anything sensitive in
 * the URL, unlike the old ?key= scheme. */
export async function loginSubmit(request: Request, env: Env) {
  const formData = await request.formData();
  const username = String(formData.get("username") ?? "");
  const password = String(formData.get("password") ?? "");

  const admin = await findAdminByUsername(env, username);
  const valid = admin ? await verifyPassword(password, admin.password_hash) : false;

  if (!admin || !valid) {
    return Response.redirect(new URL("/admin/login?error=1", request.url).toString(), 303);
  }

  const setCookie = await createAdminSession(env, admin.id);
  return new Response(null, {
    status: 303,
    headers: { location: new URL("/admin/pair", request.url).toString(), "set-cookie": setCookie },
  });
}

/** POST /admin/logout — deletes the session row (so the cookie can't be replayed even if it leaks
 * later) and clears the cookie client-side. */
export async function logout(request: Request, env: Env) {
  const cookieHeader = request.headers.get("cookie") ?? "";
  const match = /admin_session=([^;]+)/.exec(cookieHeader);
  if (match) {
    const tokenHash = await sha256Hex(match[1]!);
    await env.DB.prepare("DELETE FROM photobooth_admin_sessions WHERE token_hash = ?").bind(tokenHash).run();
  }

  return new Response(null, {
    status: 303,
    headers: { location: new URL("/admin/login", request.url).toString(), "set-cookie": clearAdminSessionCookie() },
  });
}

/** POST /admin/setup — one-time bootstrap for the very first admin account, gated by the deploy-time
 * ADMIN_API_KEY secret (set via `wrangler secret put`). Refuses once any admin exists — after that,
 * use the logged-in "Přidat admina" form at /admin/users instead. Intended to be called once with
 * curl/httpie right after a fresh deploy, not from a browser. */
export async function setupAdmin(request: Request, env: Env) {
  const failure = requireAdminKey(request, env);
  if (failure) {
    throw new StatusError(failure.status, { error: failure.error });
  }

  const { count } = (await env.DB.prepare("SELECT COUNT(*) AS count FROM photobooth_admins").first<{ count: number }>()) ?? {
    count: 0,
  };
  if (count > 0) {
    throw new StatusError(409, { error: "An admin account already exists. Log in and use /admin/users to add more." });
  }

  const body = await request.json<{ username?: string; password?: string }>().catch(() => ({}) as { username?: string; password?: string });
  const username = (body.username ?? "").trim();
  const password = body.password ?? "";
  if (username.length < 3 || password.length < 8) {
    throw new StatusError(400, { error: "username must be >= 3 chars and password >= 8 chars." });
  }

  const id = createId();
  await env.DB
    .prepare("INSERT INTO photobooth_admins (id, username, password_hash, created_at) VALUES (?, ?, ?, ?)")
    .bind(id, username, await hashPassword(password), new Date().toISOString())
    .run();

  return { ok: true, id, username };
}

function usersPageHtml(admins: AdminRow[], error?: string): string {
  const rows = admins
    .map((admin) => `<tr><td>${admin.username}</td><td>${new Date(admin.created_at).toLocaleString("cs-CZ")}</td></tr>`)
    .join("");

  return `<!doctype html>
<html lang="cs"><head><meta charset="utf-8"><title>Správa účtů</title>
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<style>
  body { font-family: system-ui, sans-serif; background: #0d1b2e; color: #f5f5f5; padding: 32px; }
  a { color: #d4af37; }
  table { border-collapse: collapse; width: 100%; max-width: 480px; margin: 16px 0 32px; }
  td, th { padding: 8px 12px; border-bottom: 1px solid #223650; text-align: left; }
  input, button { padding: 6px 10px; font-size: 1rem; }
  button { background: #d4af37; color: #0d1b2e; border: none; border-radius: 6px; cursor: pointer; font-weight: 700; }
  .error { color: #e0a458; }
  form.logout { display: inline; margin-left: 12px; }
</style></head>
<body>
  <p>
    <a href="/admin/pair">Párování zařízení</a> &middot;
    <a href="/admin/stats">Statistiky</a> &middot;
    <a href="/admin/gallery">Přehled fotografií</a>
    <form class="logout" method="post" action="/admin/logout"><button type="submit">Odhlásit se</button></form>
  </p>
  <h1>Účty administrátorů</h1>
  <table><thead><tr><th>Jméno</th><th>Vytvořen</th></tr></thead><tbody>${rows}</tbody></table>
  ${error ? `<p class="error">${error}</p>` : ""}
  <h2>Přidat admina</h2>
  <form method="post" action="/admin/users">
    <label>Uživatelské jméno <input type="text" name="username" required></label>
    <label>Heslo <input type="password" name="password" required minlength="8"></label>
    <button type="submit">Vytvořit</button>
  </form>
</body></html>`;
}

/** GET /admin/users — session-protected list of admin accounts + a form to add another one, so a
 * second staff member can get their own login without anyone touching the database directly. */
export async function usersPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const { results: admins } = await env.DB
    .prepare("SELECT * FROM photobooth_admins ORDER BY created_at ASC")
    .all<AdminRow>();

  return new Response(usersPageHtml(admins), { headers: { "content-type": "text/html; charset=utf-8" } });
}

/** POST /admin/users — adds another admin account. Requires an existing session (not the bootstrap
 * key), so only someone already logged in can create more accounts. */
export async function createUser(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const formData = await request.formData();
  const username = String(formData.get("username") ?? "").trim();
  const password = String(formData.get("password") ?? "");

  const { results: admins } = await env.DB
    .prepare("SELECT * FROM photobooth_admins ORDER BY created_at ASC")
    .all<AdminRow>();

  if (username.length < 3 || password.length < 8) {
    return new Response(usersPageHtml(admins, "Jméno musí mít alespoň 3 znaky, heslo alespoň 8."), {
      status: 400,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  }

  if (await findAdminByUsername(env, username)) {
    return new Response(usersPageHtml(admins, "Toto uživatelské jméno už existuje."), {
      status: 409,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  }

  await env.DB
    .prepare("INSERT INTO photobooth_admins (id, username, password_hash, created_at) VALUES (?, ?, ?, ?)")
    .bind(createId(), username, await hashPassword(password), new Date().toISOString())
    .run();

  return Response.redirect(new URL("/admin/users", request.url).toString(), 303);
}
