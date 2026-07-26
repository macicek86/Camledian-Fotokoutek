export interface Env {
  DB: D1Database;
  ASSETS_BUCKET: R2Bucket;

  DEFAULT_PHOTO_RETENTION_DAYS: string;
  GALLERY_BASE_URL: string;
  R2_BUCKET_NAME: string;

  // Secrets — set via `wrangler secret put`, never committed (see .env.example).
  ADMIN_API_KEY?: string;
  R2_ACCESS_KEY_ID?: string;
  R2_SECRET_ACCESS_KEY?: string;
  R2_ACCOUNT_ID?: string;
}

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
