ALTER TYPE ged.audit_action_enum ADD VALUE IF NOT EXISTS 'HTTP';
CREATE INDEX IF NOT EXISTS ix_document_tenant_folder_created ON ged.document (tenant_id, folder_id, created_at DESC);
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'ged'
          AND table_name = 'ocr_job'
          AND column_name = 'document_version_id'
    ) THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job (tenant_id, document_version_id, status, requested_at DESC)';
    ELSIF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'ged'
          AND table_name = 'ocr_job'
          AND column_name = 'version_id'
    ) THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job (tenant_id, /* legacy compatibility */ version_id, status, requested_at DESC)';
    ELSE
        RAISE NOTICE 'Índice ix_ocr_job_tenant_version_status não criado: ged.ocr_job não possui document_version_id nem version_id.';
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_doc_class_tenant_status ON ged.document_classification (tenant_id, status, created_at DESC);
DO $$
BEGIN
    IF to_regclass('ged.ocr_job') IS NOT NULL
       AND to_regclass('ged.mv_dashboard_ocr') IS NULL THEN
        EXECUTE '
            CREATE MATERIALIZED VIEW ged.mv_dashboard_ocr AS
            SELECT tenant_id, status, count(*) AS total
            FROM ged.ocr_job
            GROUP BY tenant_id, status
        ';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('ged.mv_dashboard_ocr') IS NOT NULL THEN
        EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS ix_mv_dashboard_ocr ON ged.mv_dashboard_ocr (tenant_id, status)';
    END IF;
END $$;
CREATE TABLE IF NOT EXISTS ged.app_cache_control(cache_key text primary key, updated_at timestamptz not null default now());
INSERT INTO ged.app_cache_control(cache_key) VALUES ('ClassificationDashboard/Count') ON CONFLICT (cache_key) DO UPDATE SET updated_at = now();
