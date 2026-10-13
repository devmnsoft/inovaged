-- Tentativa de preview é uma execução identificável.
-- requested_at permanece a solicitação original.
-- attempt_started_at marca o início da tentativa corrente.
-- finished_at marca o término terminal.
-- outcome_confirmed_at marca a confirmação do resultado READY.
-- requested_at_kind distingue data comprovada de valor reconstruído ou de primeira observação.
-- Registros antigos não são promovidos a data histórica exata.

ALTER TABLE IF EXISTS ged.preview_status ADD COLUMN IF NOT EXISTS current_attempt_id uuid NULL;
ALTER TABLE IF EXISTS ged.preview_status ADD COLUMN IF NOT EXISTS attempt_started_at timestamptz NULL;
ALTER TABLE IF EXISTS ged.preview_status ADD COLUMN IF NOT EXISTS persistence_error text NULL;
ALTER TABLE IF EXISTS ged.preview_status ADD COLUMN IF NOT EXISTS pending_preview_path text NULL;
ALTER TABLE IF EXISTS ged.preview_status ADD COLUMN IF NOT EXISTS outcome_confirmed_at timestamptz NULL;
ALTER TABLE IF EXISTS ged.preview_status ADD COLUMN IF NOT EXISTS requested_at_kind text;

DO $$
BEGIN
    IF to_regclass('ged.preview_status') IS NULL THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'ged' AND table_name = 'preview_status' AND column_name = 'last_attempt_at'
    ) THEN
        UPDATE ged.preview_status
        SET requested_at_kind = CASE
            WHEN requested_at IS NOT NULL AND (
                (last_attempt_at IS NOT NULL AND requested_at = last_attempt_at)
                OR (finished_at IS NOT NULL AND requested_at = finished_at)
            ) THEN 'RECONSTRUCTED'
            ELSE 'UNSPECIFIED'
        END
        WHERE requested_at_kind IS NULL;
    ELSE
        UPDATE ged.preview_status
        SET requested_at_kind = 'UNSPECIFIED'
        WHERE requested_at_kind IS NULL;
    END IF;
END $$;

ALTER TABLE IF EXISTS ged.preview_status ALTER COLUMN requested_at_kind SET DEFAULT 'UNSPECIFIED';
ALTER TABLE IF EXISTS ged.preview_status ALTER COLUMN requested_at_kind SET NOT NULL;

DO $$
BEGIN
    IF to_regclass('ged.preview_status') IS NULL THEN
        RETURN;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_preview_status_requested_at_kind'
    ) THEN
        ALTER TABLE ged.preview_status
            ADD CONSTRAINT ck_preview_status_requested_at_kind
            CHECK (requested_at_kind IN ('ORIGINAL', 'RECONSTRUCTED', 'FIRST_OBSERVATION', 'UNSPECIFIED'));
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('ged.processing_job') IS NULL THEN
        RETURN;
    END IF;

    ALTER TABLE ged.processing_job
        ADD COLUMN IF NOT EXISTS payload jsonb NOT NULL DEFAULT '{}'::jsonb;

    CREATE INDEX IF NOT EXISTS ix_processing_job_type_status_lock
        ON ged.processing_job (job_type, status, locked_at);
END $$;
