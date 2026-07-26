-- Standalone admin login (replaces the shared ADMIN_API_KEY for the human-facing /admin/* pages;
-- the key itself lives on as a bootstrap/server-to-server credential, see lib/auth.ts).

CREATE TABLE photobooth_admins (
    id            TEXT PRIMARY KEY,
    username      TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    created_at    TEXT NOT NULL
);

CREATE TABLE photobooth_admin_sessions (
    token_hash TEXT PRIMARY KEY,
    admin_id   TEXT NOT NULL REFERENCES photobooth_admins(id),
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL
);
CREATE INDEX idx_admin_sessions_expires_at ON photobooth_admin_sessions(expires_at);
