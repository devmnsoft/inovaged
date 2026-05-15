ALTER TYPE ged.audit_action_enum ADD VALUE IF NOT EXISTS 'HTTP';
CREATE INDEX IF NOT EXISTS ix_document_tenant_folder_created ON ged.document (tenant_id, folder_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job (tenant_id, version_id, status, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_doc_class_tenant_status ON ged.document_classification (tenant_id, status, created_at DESC);
CREATE MATERIALIZED VIEW IF NOT EXISTS ged.mv_dashboard_ocr AS
SELECT tenant_id, status, count(*) AS total FROM ged.ocr_job GROUP BY tenant_id, status;
CREATE UNIQUE INDEX IF NOT EXISTS ix_mv_dashboard_ocr ON ged.mv_dashboard_ocr (tenant_id, status);
CREATE TABLE IF NOT EXISTS ged.app_cache_control(cache_key text primary key, updated_at timestamptz not null default now());
INSERT INTO ged.app_cache_control(cache_key) VALUES ('ClassificationDashboard/Count') ON CONFLICT (cache_key) DO UPDATE SET updated_at = now();
