const ESCAPE_MAP: Record<string, string> = {
  "&": "&amp;",
  "<": "&lt;",
  ">": "&gt;",
  '"': "&quot;",
  "'": "&#39;",
};

/** Escapes text for safe interpolation into an HTML template — every admin page builds markup by
 * string concatenation (no JSX/templating engine here), so anything sourced from the DB (usernames,
 * device/event names, ...) must go through this before it lands in a response. */
export function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (char) => ESCAPE_MAP[char]!);
}
