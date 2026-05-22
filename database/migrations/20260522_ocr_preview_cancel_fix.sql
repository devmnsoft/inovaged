DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema='ged' AND table_name='preview_status'
    ) THEN
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema='ged' AND table_name='preview_status' AND column_name='preview_path'
        ) THEN
            ALTER TABLE ged.preview_status ADD COLUMN preview_path text NULL;
        END IF;
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS preview_error text NULL;
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS preview_generated_at timestamptz NULL;
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS preview_attempts integer NOT NULL DEFAULT 0;
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS preview_last_attempt_at timestamptz NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema='ged' AND table_name='ocr_job'
    ) THEN
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS cancel_requested boolean NOT NULL DEFAULT false;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS cancel_requested_at timestamptz NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS cancelled_by uuid NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS cancel_reason text NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace
        WHERE n.nspname='ged' AND t.typname='ocr_status_enum'
    ) AND NOT EXISTS (
        SELECT 1 FROM pg_enum e
        JOIN pg_type t ON t.oid=e.enumtypid
        JOIN pg_namespace n ON n.oid=t.typnamespace
        WHERE n.nspname='ged' AND t.typname='ocr_status_enum' AND e.enumlabel='CANCELLED'
    ) THEN
        ALTER TYPE ged.ocr_status_enum ADD VALUE 'CANCELLED';
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_preview_status_tenant_version ON ged.preview_status (tenant_id, document_version_id);
CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job (tenant_id, document_version_id, status);
