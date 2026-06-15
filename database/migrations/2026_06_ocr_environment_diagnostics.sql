DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='ged' AND table_name='ocr_job') THEN
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS error_details_json jsonb NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS started_at timestamptz NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS attempt_count int NOT NULL DEFAULT 0;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS worker_id text NULL;
    END IF;
END $$;
