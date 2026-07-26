import { StatusError, type IRequest } from "itty-router";
import { renderAdminShell } from "../lib/adminLayout";
import { clearAdminSessionCookie, createAdminSession, findAdminById, findAdminByUsername, requireAdminKey, requireAdminSession } from "../lib/auth";
import { escapeHtml } from "../lib/html";
import { createId, sha256Hex } from "../lib/ids";
import { hashPassword, verifyPassword } from "../lib/password";
import type { AdminRow, Env } from "../types";

function html(body: string, status = 200): Response {
  return new Response(body, { status, headers: { "content-type": "text/html; charset=utf-8" } });
}

function loginPageHtml(error?: string): string {
  return `<!doctype html>
<html lang="cs">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Přihlášení — Camledian Fotokoutek</title>
<meta name="robots" content="noindex, nofollow">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,600&family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="/admin.css">
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
</head>
<body>
  <div class="auth-screen">
    <div class="auth-card">
      <img src="/favicon-32.png" alt="">
      <h1>Administrace fotokoutku</h1>
      ${error ? `<p class="error">${escapeHtml(error)}</p>` : ""}
      <form method="post" action="/admin/login">
        <label for="username">Uživatelské jméno</label>
        <input type="text" id="username" name="username" autocomplete="username" required autofocus>
        <label for="password">Heslo</label>
        <input type="password" id="password" name="password" autocomplete="current-password" required>
        <button type="submit">Přihlásit se</button>
      </form>
    </div>
  </div>
</body></html>`;
}

/** GET /admin/login — plain login form; the human-facing counterpart to requireAdminKey (spec §56
 * eventually wants this wired to Camledian's own administration, but per the owner that's not
 * happening yet — this needs to stand on its own). */
export async function loginPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (session.ok) {
    return Response.redirect(new URL("/admin/stats", request.url).toString(), 303);
  }

  const url = new URL(request.url);
  const error = url.searchParams.get("error") === "1" ? "Nesprávné jméno nebo heslo." : undefined;
  return html(loginPageHtml(error));
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
    headers: { location: new URL("/admin/stats", request.url).toString(), "set-cookie": setCookie },
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

const NOTICES: Record<string, string> = {
  "password-changed": "Heslo bylo změněno.",
  "password-reset": "Heslo účtu bylo resetováno.",
  "user-deleted": "Účet byl smazán.",
  "user-created": "Účet byl vytvořen.",
};

interface UsersPageState {
  currentAdminId: string;
  currentUsername: string;
  createError?: string;
  accountError?: string;
  notice?: string;
}

function usersPageHtml(admins: AdminRow[], state: UsersPageState): string {
  const rows = admins
    .map((admin) => {
      const isSelf = admin.id === state.currentAdminId;
      return `
      <tr>
        <td>${escapeHtml(admin.username)} ${isSelf ? '<span class="badge you">vy</span>' : ""}</td>
        <td>${new Date(admin.created_at).toLocaleString("cs-CZ")}</td>
        <td class="actions-row">
          <form class="inline" method="post" action="/admin/users/${admin.id}/reset-password">
            <input type="password" name="password" placeholder="nové heslo" required minlength="8">
            <button type="submit" class="secondary small">Resetovat heslo</button>
          </form>
          ${
            isSelf
              ? ""
              : `<form method="post" action="/admin/users/${admin.id}/delete" data-confirm="Opravdu smazat účet ${escapeHtml(admin.username)}?">
                   <button type="submit" class="danger small">Smazat</button>
                 </form>`
          }
        </td>
      </tr>`;
    })
    .join("");

  const body = `
    ${state.notice ? `<p class="notice">${escapeHtml(state.notice)}</p>` : ""}
    <div class="panel">
      <h2>Můj účet</h2>
      <p>Přihlášen jako <strong>${escapeHtml(state.currentUsername)}</strong>.</p>
      ${state.accountError ? `<p class="error">${escapeHtml(state.accountError)}</p>` : ""}
      <form method="post" action="/admin/account/password">
        <label for="currentPassword">Současné heslo</label>
        <input type="password" id="currentPassword" name="currentPassword" autocomplete="current-password" required>
        <label for="newPassword">Nové heslo</label>
        <input type="password" id="newPassword" name="newPassword" autocomplete="new-password" required minlength="8">
        <button type="submit">Změnit heslo</button>
      </form>
    </div>
    <div class="panel">
      <h2>Administrátoři</h2>
      <table><thead><tr><th>Jméno</th><th>Vytvořen</th><th>Akce</th></tr></thead><tbody>${rows}</tbody></table>
    </div>
    <div class="panel">
      <h2>Přidat admina</h2>
      ${state.createError ? `<p class="error">${escapeHtml(state.createError)}</p>` : ""}
      <form method="post" action="/admin/users">
        <label for="newUsername">Uživatelské jméno</label>
        <input type="text" id="newUsername" name="username" required>
        <label for="newUserPassword">Heslo</label>
        <input type="password" id="newUserPassword" name="password" required minlength="8">
        <button type="submit">Vytvořit účet</button>
      </form>
    </div>`;

  return renderAdminShell({ title: "Účty", active: "users", adminUsername: state.currentUsername, body });
}

async function loadAdmins(env: Env): Promise<AdminRow[]> {
  const { results } = await env.DB.prepare("SELECT * FROM photobooth_admins ORDER BY created_at ASC").all<AdminRow>();
  return results;
}

/** GET /admin/users — session-protected list of admin accounts, self-service password change, and
 * per-account "reset password" / "delete" actions, so a team can manage its own logins without
 * anyone touching the database directly. */
export async function usersPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const [admins, self] = await Promise.all([loadAdmins(env), findAdminById(env, session.adminId)]);
  const noticeCode = new URL(request.url).searchParams.get("notice");

  return html(
    usersPageHtml(admins, {
      currentAdminId: session.adminId,
      currentUsername: self?.username ?? "",
      notice: noticeCode ? NOTICES[noticeCode] : undefined,
    }),
  );
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

  const [admins, self] = await Promise.all([loadAdmins(env), findAdminById(env, session.adminId)]);
  const state: UsersPageState = { currentAdminId: session.adminId, currentUsername: self?.username ?? "" };

  if (username.length < 3 || password.length < 8) {
    return html(usersPageHtml(admins, { ...state, createError: "Jméno musí mít alespoň 3 znaky, heslo alespoň 8." }), 400);
  }

  if (await findAdminByUsername(env, username)) {
    return html(usersPageHtml(admins, { ...state, createError: "Toto uživatelské jméno už existuje." }), 409);
  }

  await env.DB
    .prepare("INSERT INTO photobooth_admins (id, username, password_hash, created_at) VALUES (?, ?, ?, ?)")
    .bind(createId(), username, await hashPassword(password), new Date().toISOString())
    .run();

  return Response.redirect(new URL("/admin/users?notice=user-created", request.url).toString(), 303);
}

/** POST /admin/account/password — self-service password change for the logged-in admin; requires
 * the current password (unlike the admin-to-admin reset below), same as any normal account. */
export async function changeOwnPassword(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const formData = await request.formData();
  const currentPassword = String(formData.get("currentPassword") ?? "");
  const newPassword = String(formData.get("newPassword") ?? "");

  const [admins, self] = await Promise.all([loadAdmins(env), findAdminById(env, session.adminId)]);
  const state: UsersPageState = { currentAdminId: session.adminId, currentUsername: self?.username ?? "" };

  if (!self || !(await verifyPassword(currentPassword, self.password_hash))) {
    return html(usersPageHtml(admins, { ...state, accountError: "Současné heslo není správně." }), 400);
  }

  if (newPassword.length < 8) {
    return html(usersPageHtml(admins, { ...state, accountError: "Nové heslo musí mít alespoň 8 znaků." }), 400);
  }

  await env.DB
    .prepare("UPDATE photobooth_admins SET password_hash = ? WHERE id = ?")
    .bind(await hashPassword(newPassword), session.adminId)
    .run();

  return Response.redirect(new URL("/admin/users?notice=password-changed", request.url).toString(), 303);
}

/** POST /admin/users/:id/reset-password — lets any logged-in admin set a new password for any
 * account (including their own, though /admin/account/password is the intended path for that) —
 * same trust model as the existing "create another admin" form: anyone with a session is already
 * fully trusted staff. Useful when a colleague forgets their password and can't log in to change it
 * themselves. */
export async function resetUserPassword(request: IRequest, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const targetId = request.params.id!;
  const formData = await request.formData();
  const password = String(formData.get("password") ?? "");

  if (password.length < 8) {
    const [admins, self] = await Promise.all([loadAdmins(env), findAdminById(env, session.adminId)]);
    return html(
      usersPageHtml(admins, {
        currentAdminId: session.adminId,
        currentUsername: self?.username ?? "",
        createError: "Nové heslo musí mít alespoň 8 znaků.",
      }),
      400,
    );
  }

  await env.DB.prepare("UPDATE photobooth_admins SET password_hash = ? WHERE id = ?").bind(await hashPassword(password), targetId).run();

  return Response.redirect(new URL("/admin/users?notice=password-reset", request.url).toString(), 303);
}

/** POST /admin/users/:id/delete — removes an admin account. Refuses to delete the caller's own
 * account (rather than separately tracking "is this the last admin" — since every deletion needs a
 * *different*, still-existing admin to perform it, this alone guarantees at least one account always
 * remains). Also clears the deleted account's sessions so a still-open browser tab is logged out. */
export async function deleteUser(request: IRequest, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const targetId = request.params.id!;
  if (targetId === session.adminId) {
    throw new StatusError(400, { error: "Nelze smazat vlastní účet." });
  }

  await env.DB.prepare("DELETE FROM photobooth_admin_sessions WHERE admin_id = ?").bind(targetId).run();
  await env.DB.prepare("DELETE FROM photobooth_admins WHERE id = ?").bind(targetId).run();

  return Response.redirect(new URL("/admin/users?notice=user-deleted", request.url).toString(), 303);
}
