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
            WHERE n.nspname = 'ged' AND t.typname = 'preview_processing_status' AND e.enumlabel = 'ERROR'
        ) THEN
            ALTER TYPE ged.preview_processing_status ADD VALUE 'ERROR';
        END IF;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS ged.preview_status (
    tenant_id uuid NOT NULL,
    document_version_id uuid NOT NULL,
    status ged.preview_processing_status NOT NULL DEFAULT 'PENDING',
    preview_path text NULL,
    error_message text NULL,
    requested_at timestamptz NULL,
    finished_at timestamptz NULL,
    PRIMARY KEY (tenant_id, document_version_id)
);

ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS preview_path text NULL;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS error_message text NULL;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS requested_at timestamptz NULL;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz NULL;
ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS cancel_requested boolean NOT NULL DEFAULT false;

DO $$
DECLARE duplicate_count integer;
DECLARE null_count integer;
BEGIN
    SELECT count(*) INTO duplicate_count
    FROM (
        SELECT tenant_id, document_version_id
        FROM ged.preview_status
        GROUP BY tenant_id, document_version_id
        HAVING count(*) > 1
    ) duplicates;

    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'ged.preview_status possui % chaves tenant/version duplicadas; resolva as duplicidades antes de aplicar o contrato de upsert.', duplicate_count
            USING ERRCODE = 'P0001';
    END IF;

    SELECT count(*) INTO null_count
    FROM ged.preview_status
    WHERE tenant_id IS NULL OR document_version_id IS NULL;

    IF null_count > 0 THEN
        RAISE EXCEPTION 'ged.preview_status possui % registros sem tenant_id ou document_version_id; resolva antes de aplicar o contrato de upsert.', null_count
            USING ERRCODE = 'P0001';
    END IF;
END $$;

ALTER TABLE ged.preview_status ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE ged.preview_status ALTER COLUMN document_version_id SET NOT NULL;
ALTER TABLE ged.preview_status ALTER COLUMN status SET NOT NULL;
ALTER TABLE ged.preview_status ALTER COLUMN status SET DEFAULT 'PENDING';

CREATE UNIQUE INDEX IF NOT EXISTS ux_preview_status_tenant_version
    ON ged.preview_status (tenant_id, document_version_id);

CREATE INDEX IF NOT EXISTS ix_preview_status_tenant_status
    ON ged.preview_status (tenant_id, status);
