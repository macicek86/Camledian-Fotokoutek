-- Lets an admin "unpair" a device from the new /admin/devices page without deleting its row —
-- photos and events reference device_id by foreign key, and the row's heartbeat/pairing history is
-- worth keeping for audit purposes. A revoked device simply fails auth from then on.

ALTER TABLE photobooth_devices ADD COLUMN revoked_at TEXT;
