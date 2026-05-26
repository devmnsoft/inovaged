DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='ged' AND table_name='ocr_job') THEN
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS error_message text NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_status_next_attempt
ON ged.ocr_job(tenant_id, status, next_attempt_at, requested_at);

CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version
ON ged.ocr_job(tenant_id, document_version_id, requested_at desc);
