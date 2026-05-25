DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema='ged' AND table_name='preview_status'
    ) THEN
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS error_message text NULL;
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz NULL;
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
        ALTER TABLE ged.preview_status ADD COLUMN IF NOT EXISTS cancel_requested boolean NOT NULL DEFAULT false;
    END IF;
END $$;
