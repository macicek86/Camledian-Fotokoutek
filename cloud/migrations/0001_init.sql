-- Initial schema (spec §33). UUIDs everywhere, ISO-8601 UTC text for timestamps (D1/SQLite has no
-- native datetime type).

CREATE TABLE photobooth_devices (
    id                TEXT PRIMARY KEY,
    name              TEXT,
    token_hash        TEXT NOT NULL,
    paired_at         TEXT NOT NULL,
    last_heartbeat_at TEXT,
    last_status       TEXT
);

CREATE TABLE photobooth_events (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    output_template_id TEXT NOT NULL DEFAULT 'digital-landscape',
    is_active         INTEGER NOT NULL DEFAULT 1,
    device_id         TEXT REFERENCES photobooth_devices(id),
    created_at        TEXT NOT NULL
);

CREATE TABLE photobooth_backgrounds (
    id         TEXT PRIMARY KEY,
    event_id   TEXT NOT NULL REFERENCES photobooth_events(id),
    name       TEXT NOT NULL,
    r2_key     TEXT NOT NULL,
    hash       TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE photobooth_overlays (
    id         TEXT PRIMARY KEY,
    event_id   TEXT NOT NULL REFERENCES photobooth_events(id),
    name       TEXT NOT NULL,
    r2_key     TEXT NOT NULL,
    hash       TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE photobooth_photos (
    id             TEXT PRIMARY KEY,
    device_id      TEXT NOT NULL REFERENCES photobooth_devices(id),
    event_id       TEXT REFERENCES photobooth_events(id),
    r2_key         TEXT NOT NULL,
    content_type   TEXT NOT NULL DEFAULT 'image/jpeg',
    status         TEXT NOT NULL DEFAULT 'pending-upload', -- pending-upload | uploaded | expired
    download_token TEXT UNIQUE,
    created_at     TEXT NOT NULL,
    uploaded_at    TEXT,
    expires_at     TEXT
);
CREATE INDEX idx_photos_download_token ON photobooth_photos(download_token);
CREATE INDEX idx_photos_expires_at ON photobooth_photos(expires_at);

CREATE TABLE photobooth_pairing_codes (
    code         TEXT PRIMARY KEY,
    status       TEXT NOT NULL DEFAULT 'pending', -- pending | confirmed | expired
    device_id    TEXT REFERENCES photobooth_devices(id),
    device_token TEXT,
    created_at   TEXT NOT NULL,
    expires_at   TEXT NOT NULL,
    confirmed_at TEXT
);
