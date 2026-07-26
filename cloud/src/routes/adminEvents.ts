import type { IRequest } from "itty-router";
import { renderAdminShell } from "../lib/adminLayout";
import { findAdminById, requireAdminSession } from "../lib/auth";
import { escapeHtml } from "../lib/html";
import { createId } from "../lib/ids";
import type { DeviceRow, Env } from "../types";

// Mirrors Camledian.Photobooth.Core.Models.OutputTemplate.BuiltIn on the Windows app side — the
// cloud side only needs to record *which* template an event uses, not the pixel geometry.
const TEMPLATES = [
  { id: "digital-landscape", label: "Digital 16:9" },
  { id: "photo-10x15-portrait", label: "Foto 10×15 na výšku" },
  { id: "photo-10x15-landscape", label: "Foto 10×15 na šířku" },
] as const;

interface EventListRow {
  id: string;
  name: string;
  output_template_id: string;
  is_active: number;
  device_id: string | null;
  device_name: string | null;
  photo_count: number;
  created_at: string;
}

function templateLabel(id: string): string {
  return TEMPLATES.find((t) => t.id === id)?.label ?? id;
}

function templateOptions(selected: string): string {
  return TEMPLATES.map((t) => `<option value="${t.id}" ${t.id === selected ? "selected" : ""}>${t.label}</option>`).join("");
}

function deviceOptions(devices: DeviceRow[], selected: string | null): string {
  const options = devices
    .map((d) => `<option value="${d.id}" ${d.id === selected ? "selected" : ""}>${escapeHtml(d.name ?? d.id)}</option>`)
    .join("");
  return `<option value="" ${selected === null ? "selected" : ""}>Sdílená (všechna zařízení)</option>${options}`;
}

function eventsPageHtml(adminUsername: string, events: EventListRow[], devices: DeviceRow[], error?: string): string {
  const rows = events
    .map(
      (event) => `
      <tr>
        <td colspan="6" style="padding:0; border-bottom: 1px solid var(--hairline);">
          <form class="inline" style="width:100%; padding:10px 14px; flex-wrap:wrap" method="post" action="/admin/events/${event.id}">
            <input type="text" name="name" value="${escapeHtml(event.name)}" style="min-width:160px">
            <select name="templateId">${templateOptions(event.output_template_id)}</select>
            <select name="deviceId">${deviceOptions(devices, event.device_id)}</select>
            <span class="badge ${event.is_active ? "on" : "off"}"><span class="dot"></span>${event.is_active ? "aktivní" : "neaktivní"}</span>
            <span style="color:var(--text-muted); font-size:0.85rem">${event.photo_count} fotek · založeno ${new Date(event.created_at).toLocaleDateString("cs-CZ")}</span>
            <button type="submit" class="secondary small">Uložit</button>
          </form>
          <form method="post" action="/admin/events/${event.id}/toggle" style="padding:0 14px 10px">
            <button type="submit" class="small">${event.is_active ? "Deaktivovat" : "Aktivovat"}</button>
          </form>
        </td>
      </tr>`,
    )
    .join("");

  const body = `
    <div class="panel">
      <h2>Akce</h2>
      <div class="table-scroll">
        <table>
          <tbody>${rows || '<tr><td class="empty">Zatím žádné akce.</td></tr>'}</tbody>
        </table>
      </div>
    </div>
    <div class="panel">
      <h2>Nová akce</h2>
      ${error ? `<p class="error">${escapeHtml(error)}</p>` : ""}
      <form method="post" action="/admin/events">
        <label for="name">Název akce</label>
        <input type="text" id="name" name="name" required placeholder="např. Svatba Nováková 12. 9.">
        <label for="templateId">Výstupní šablona</label>
        <select id="templateId" name="templateId">${templateOptions(TEMPLATES[0].id)}</select>
        <label for="deviceId">Zařízení</label>
        <select id="deviceId" name="deviceId">${deviceOptions(devices, null)}</select>
        <label><input type="checkbox" name="isActive" value="1" checked style="width:auto"> Aktivní hned po založení</label>
        <button type="submit">Založit akci</button>
      </form>
    </div>`;

  return renderAdminShell({ title: "Akce", active: "events", adminUsername, body });
}

async function loadEvents(env: Env): Promise<EventListRow[]> {
  const { results } = await env.DB
    .prepare(
      `SELECT
         e.id, e.name, e.output_template_id, e.is_active, e.device_id, e.created_at,
         d.name AS device_name,
         COUNT(p.id) AS photo_count
       FROM photobooth_events e
       LEFT JOIN photobooth_devices d ON d.id = e.device_id
       LEFT JOIN photobooth_photos p ON p.event_id = e.id
       GROUP BY e.id
       ORDER BY e.created_at DESC`,
    )
    .all<EventListRow>();
  return results;
}

async function loadDevices(env: Env): Promise<DeviceRow[]> {
  const { results } = await env.DB
    .prepare("SELECT * FROM photobooth_devices WHERE revoked_at IS NULL ORDER BY paired_at DESC")
    .all<DeviceRow>();
  return results;
}

/**
 * GET /admin/events — lets staff create/edit/(de)activate events from the browser. Previously the
 * only way a row landed in `photobooth_events` was a manual INSERT — there was no admin route for it
 * at all — even though events drive both what a paired device syncs (routes/config.ts) and how
 * uploaded photos get grouped in stats/gallery.
 */
export async function adminEventsPage(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const [events, devices, self] = await Promise.all([loadEvents(env), loadDevices(env), findAdminById(env, session.adminId)]);

  return new Response(eventsPageHtml(self?.username ?? "", events, devices), {
    headers: { "content-type": "text/html; charset=utf-8" },
  });
}

/** POST /admin/events — creates a new event. */
export async function createEvent(request: Request, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const formData = await request.formData();
  const name = String(formData.get("name") ?? "").trim();
  const templateId = String(formData.get("templateId") ?? TEMPLATES[0].id);
  const deviceId = String(formData.get("deviceId") ?? "") || null;
  const isActive = formData.get("isActive") ? 1 : 0;

  if (!name) {
    const [events, devices, self] = await Promise.all([loadEvents(env), loadDevices(env), findAdminById(env, session.adminId)]);
    return new Response(eventsPageHtml(self?.username ?? "", events, devices, "Název akce je povinný."), {
      status: 400,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  }

  await env.DB
    .prepare(
      "INSERT INTO photobooth_events (id, name, output_template_id, is_active, device_id, created_at) VALUES (?, ?, ?, ?, ?, ?)",
    )
    .bind(createId(), name, templateId, isActive, deviceId, new Date().toISOString())
    .run();

  return Response.redirect(new URL("/admin/events", request.url).toString(), 303);
}

/** POST /admin/events/:id — updates name / output template / device assignment. */
export async function updateEvent(request: IRequest, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  const formData = await request.formData();
  const name = String(formData.get("name") ?? "").trim();
  const templateId = String(formData.get("templateId") ?? TEMPLATES[0].id);
  const deviceId = String(formData.get("deviceId") ?? "") || null;

  if (name) {
    await env.DB
      .prepare("UPDATE photobooth_events SET name = ?, output_template_id = ?, device_id = ? WHERE id = ?")
      .bind(name, templateId, deviceId, request.params.id)
      .run();
  }

  return Response.redirect(new URL("/admin/events", request.url).toString(), 303);
}

/** POST /admin/events/:id/toggle — flips is_active. */
export async function toggleEvent(request: IRequest, env: Env) {
  const session = await requireAdminSession(request, env);
  if (!session.ok) {
    return Response.redirect(new URL("/admin/login", request.url).toString(), 303);
  }

  await env.DB.prepare("UPDATE photobooth_events SET is_active = 1 - is_active WHERE id = ?").bind(request.params.id).run();

  return Response.redirect(new URL("/admin/events", request.url).toString(), 303);
}
