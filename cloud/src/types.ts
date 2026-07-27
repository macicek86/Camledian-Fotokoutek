/** Bindings come from `wrangler types` (worker-configuration.d.ts), generated straight from
 * wrangler.toml + .dev.vars — never hand-written, so it can't silently drift from what's actually
 * deployed. Re-run `npx wrangler types` after adding or renaming any binding. Re-exported here so
 * the rest of the code can keep importing `Env` from one place. */
export type Env = Cloudflare.Env;

export interface AuthedRequest extends Request {
  deviceId?: string;
}

export interface DeviceRow {
  id: string;
  name: string | null;
  token_hash: string;
  paired_at: string;
  last_heartbeat_at: string | null;
  last_status: string | null;
  revoked_at: string | null;
}

export interface EventRow {
  id: string;
  name: string;
  output_template_id: string;
  is_active: number;
  device_id: string | null;
  created_at: string;
}

export interface AssetRow {
  id: string;
  event_id: string;
  name: string;
  r2_key: string;
  hash: string;
  sort_order: number;
}

export interface PhotoRow {
  id: string;
  device_id: string;
  event_id: string | null;
  r2_key: string;
  content_type: string;
  status: "pending-upload" | "uploaded" | "expired";
  download_token: string | null;
  created_at: string;
  uploaded_at: string | null;
  expires_at: string | null;
}

export interface AdminRow {
  id: string;
  username: string;
  password_hash: string;
  created_at: string;
}

export interface PairingCodeRow {
  code: string;
  status: "pending" | "confirmed" | "expired";
  device_id: string | null;
  device_token: string | null;
  created_at: string;
  expires_at: string;
  confirmed_at: string | null;
}
