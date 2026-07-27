-- Indexes for the lookups that run on every request rather than only in the admin console.
--
-- token_hash is what requireDeviceAuth() resolves on every single device call — config, heartbeat
-- (every 30s per device), photo create/upload/complete — and it had no index at all, so each one was
-- a full table scan. UNIQUE additionally makes it impossible for two devices to end up sharing a
-- token hash.
CREATE UNIQUE INDEX idx_devices_token_hash ON photobooth_devices(token_hash);

-- The admin gallery filters photos by event and orders them by upload time; both were unindexed.
CREATE INDEX idx_photos_event_id ON photobooth_photos(event_id);
CREATE INDEX idx_photos_status_uploaded_at ON photobooth_photos(status, uploaded_at);
