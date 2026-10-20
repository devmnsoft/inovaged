-- Additive compatibility for the bounded, owner-fenced OCR queue.
-- Reuses existing job identity, attempts, locks and status; preserves historical jobs.
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS lease_expires_at timestamptz;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS locked_by text;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS locked_at timestamptz;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS updated_at timestamptz;
CREATE INDEX IF NOT EXISTS ix_ocr_job_expired_lease
    ON ged.ocr_job (lease_expires_at, requested_at)
    WHERE status = 'PROCESSING'::ged.ocr_status_enum;
CREATE INDEX IF NOT EXISTS ix_ocr_job_pending_retry
    ON ged.ocr_job (next_attempt_at, requested_at)
    WHERE status = 'PENDING'::ged.ocr_status_enum;
