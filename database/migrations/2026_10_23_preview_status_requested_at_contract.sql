DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'ged' AND t.typname = 'preview_processing_status'
    ) THEN
        CREATE TYPE ged.preview_processing_status AS ENUM ('PENDING','PROCESSING','READY','FAILED','CANCELED','ERROR');
    ELSE
        IF NOT EXISTS (
            SELECT 1 FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = 'ged' AND t.typname = 'preview_processing_status' AND e.enumlabel = 'FAILED'
        ) THEN
            ALTER TYPE ged.preview_processing_status ADD VALUE 'FAILED';
        END IF;

        IF NOT EXISTS (
            SELECT 1 FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = 'ged' AND t.typname = 'preview_processing_status' AND e.enumlabel = 'CANCELED'
        ) THEN
            ALTER TYPE ged.preview_processing_status ADD VALUE 'CANCELED';
        END IF;

        IF NOT EXISTS (
            SELECT 1 FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = 'ged' AND t.typname = 'preview_processing_status' AND e.enumlabel = 'ERROR'
        ) THEN
            ALTER TYPE ged.preview_processing_status ADD VALUE 'ERROR';
        END IF;
    END IF;
END $$;

ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS requested_at timestamptz NULL;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz NULL;

UPDATE ged.preview_status
SET requested_at = COALESCE(last_attempt_at, finished_at, now())
WHERE requested_at IS NULL;

ALTER TABLE ged.preview_status ALTER COLUMN requested_at SET DEFAULT now();
ALTER TABLE ged.preview_status ALTER COLUMN requested_at SET NOT NULL;
ALTER TABLE ged.preview_status ALTER COLUMN status SET DEFAULT 'PENDING';
ALTER TABLE ged.preview_status ALTER COLUMN status SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_preview_status_tenant_version
    ON ged.preview_status (tenant_id, document_version_id);
