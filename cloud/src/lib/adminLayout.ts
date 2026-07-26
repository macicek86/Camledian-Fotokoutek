import { escapeHtml } from "./html";

export type AdminNavKey = "stats" | "gallery" | "devices" | "events" | "users";

interface NavItem {
  key: AdminNavKey;
  href: string;
  label: string;
  icon: string;
}

// Minimal 20x20 stroke icons (no icon font/library — this Worker has no build step, so every asset
// either ships as a static file or, for something this small, inline SVG).
const NAV_ITEMS: NavItem[] = [
  {
    key: "stats",
    href: "/admin/stats",
    label: "Přehled",
    icon: '<path d="M3 16.5V9M8.5 16.5V4M14 16.5v-6M19.5 16.5V7" stroke-linecap="round" stroke-linejoin="round"/>',
  },
  {
    key: "gallery",
    href: "/admin/gallery",
    label: "Galerie",
    icon:
      '<rect x="3" y="5.5" width="12" height="11" rx="1.5"/><path d="M7 9.5l2.2 2.6L11 10l3 4H7z" stroke-linejoin="round"/><path d="M17.5 8v6a2.5 2.5 0 0 1-2.5 2.5H8" stroke-linecap="round"/>',
  },
  {
    key: "devices",
    href: "/admin/devices",
    label: "Zařízení",
    icon: '<rect x="4" y="3.5" width="9" height="13" rx="1.5"/><circle cx="8.5" cy="14" r="0.9" fill="currentColor" stroke="none"/><path d="M15.5 7h2.2v9.5a1.3 1.3 0 0 1-1.3 1.3h-2.6" stroke-linecap="round" stroke-linejoin="round"/>',
  },
  {
    key: "events",
    href: "/admin/events",
    label: "Akce",
    icon:
      '<rect x="3" y="4.5" width="14" height="12" rx="1.5"/><path d="M3 8.5h14M7 3v3M13 3v3" stroke-linecap="round"/>',
  },
  {
    key: "users",
    href: "/admin/users",
    label: "Účty",
    icon: '<circle cx="10" cy="7" r="3"/><path d="M4 17c0-3 2.7-5 6-5s6 2 6 5" stroke-linecap="round"/>',
  },
];

const HEAD_ASSETS = `
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,500;9..144,600;9..144,700&family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
<link rel="stylesheet" href="/admin.css">
<link rel="icon" href="/favicon-32.png" sizes="32x32">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<meta name="robots" content="noindex, nofollow">`;

/** A thin horizontal rule of "sprocket holes" — the one recurring nod to the fact that this admin
 * exists to manage an actual film-strip photobooth. Used sparingly (page headers on Přehled/Galerie
 * only), never as general-purpose decoration. */
export function filmStripDivider(): string {
  return '<div class="filmstrip" aria-hidden="true"></div>';
}

export interface PhotoCardData {
  thumbUrl: string;
  publicUrl: string;
  eventName: string;
  uploadedLabel: string;
  expired: boolean;
  /** Present only when the viewer is allowed to delete the photo (used by both the gallery grid and
   * the dashboard's "recent uploads" strip — same card, same delete action). */
  deleteUrl?: string;
}

/** One "instant film" photo card — see filmStripDivider() above for the same visual idea applied to
 * the divider rule. Deliberately a <div> wrapping an <a> rather than an <a> wrapping a <form>: nesting
 * interactive content (a submit button) inside an anchor is invalid HTML and behaves inconsistently
 * across browsers. admin.js progressively turns the .thumb link into a lightbox trigger. */
export function renderPhotoCard(photo: PhotoCardData): string {
  const deleteForm = photo.deleteUrl
    ? `<div class="card-actions">
         <form method="post" action="${photo.deleteUrl}" data-confirm="Opravdu trvale smazat tuto fotografii?">
           <button type="submit" class="danger small">Smazat</button>
         </form>
       </div>`
    : "";

  return `
  <div class="photo-card" data-full="${photo.thumbUrl}" data-public="${photo.publicUrl}" data-name="${escapeHtml(photo.eventName)}" data-when="${escapeHtml(photo.uploadedLabel)}" ${photo.deleteUrl ? `data-delete-url="${photo.deleteUrl}"` : ""}>
    <a class="thumb" href="${photo.publicUrl}" target="_blank" rel="noopener">
      <img src="${photo.thumbUrl}" alt="" loading="lazy">
      <div class="cap">
        <span class="name">${escapeHtml(photo.eventName)}</span>
        <span class="when">${escapeHtml(photo.uploadedLabel)}${photo.expired ? ' &middot; <span class="expired">vypršelo</span>' : ""}</span>
      </div>
    </a>
    ${deleteForm}
  </div>`;
}

export interface AdminShellOptions {
  title: string;
  active: AdminNavKey;
  adminUsername: string;
  body: string;
  /** Extra markup injected right after <h1>, e.g. a subtitle or page-level action button. */
  headerExtra?: string;
}

/** Shared chrome for every logged-in /admin/* page: sidebar nav (collapses to a horizontal strip
 * under ~860px, see admin.css), topbar with the current account, and the content slot. Replaces the
 * ad-hoc adminNav() + inline <style> that used to be duplicated in every route file. */
export function renderAdminShell(opts: AdminShellOptions): string {
  const nav = NAV_ITEMS.map(
    (item) => `
      <a class="nav-item${item.key === opts.active ? " active" : ""}" href="${item.href}">
        <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.6">${item.icon}</svg>
        <span>${item.label}</span>
      </a>`,
  ).join("");

  return `<!doctype html>
<html lang="cs">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(opts.title)} — Camledian Fotokoutek</title>
${HEAD_ASSETS}
</head>
<body>
  <div class="shell">
    <aside class="sidebar">
      <a class="brand" href="/admin/stats">
        <img src="/favicon-32.png" alt="">
        <span>Camledian<em>Fotokoutek</em></span>
      </a>
      <nav>${nav}</nav>
      <div class="sidebar-foot">
        <a class="account-chip" href="/admin/users">${escapeHtml(opts.adminUsername)}</a>
        <form method="post" action="/admin/logout"><button type="submit" class="link-btn">Odhlásit se</button></form>
      </div>
    </aside>
    <div class="main">
      <header class="topbar">
        <h1>${escapeHtml(opts.title)}</h1>
        ${opts.headerExtra ?? ""}
      </header>
      <main class="content">
        ${opts.body}
      </main>
    </div>
  </div>
  <script src="/admin.js" defer></script>
</body>
</html>`;
}
