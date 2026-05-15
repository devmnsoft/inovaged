BEGIN;

CREATE INDEX IF NOT EXISTS idx_document_tenant_folder_created ON ged.document (tenant_id, folder_id, created_at DESC) WHERE reg_status = 'A';
CREATE INDEX IF NOT EXISTS idx_document_tenant_status_created ON ged.document (tenant_id, status, created_at DESC) WHERE reg_status = 'A';
CREATE INDEX IF NOT EXISTS idx_ocr_job_tenant_status_req ON ged.ocr_job (tenant_id, status, requested_at);
CREATE INDEX IF NOT EXISTS idx_ocr_job_version_status ON ged.ocr_job (tenant_id, document_version_id, status, requested_at DESC);
CREATE INDEX IF NOT EXISTS idx_doc_classification_pending ON ged.document_classification (tenant_id, classification_status) WHERE reg_status = 'A';

CREATE MATERIALIZED VIEW IF NOT EXISTS ged.mv_dashboard_ocr_metrics AS
SELECT tenant_id,
       count(*) FILTER (WHERE status = 'PENDING') AS pending,
       count(*) FILTER (WHERE status = 'PROCESSING') AS processing,
       count(*) FILTER (WHERE status = 'COMPLETED' AND finished_at >= now() - interval '24 hours') AS completed_24h,
       count(*) FILTER (WHERE status = 'ERROR' AND finished_at >= now() - interval '24 hours') AS error_24h
FROM ged.ocr_job
GROUP BY tenant_id;

CREATE UNIQUE INDEX IF NOT EXISTS ux_mv_dashboard_ocr_metrics_tenant ON ged.mv_dashboard_ocr_metrics (tenant_id);

CREATE TABLE IF NOT EXISTS ged.classification_dashboard_cache (
    tenant_id uuid NOT NULL,
    folder_id uuid NULL,
    pending_count integer NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, folder_id)
);

CREATE OR REPLACE FUNCTION ged.refresh_classification_dashboard_cache(p_tenant_id uuid, p_folder_id uuid DEFAULT NULL)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
  INSERT INTO ged.classification_dashboard_cache(tenant_id, folder_id, pending_count, updated_at)
  SELECT p_tenant_id, p_folder_id,
         count(*)::int,
         now()
  FROM ged.document d
  WHERE d.tenant_id = p_tenant_id
    AND d.reg_status = 'A'
    AND (p_folder_id IS NULL OR d.folder_id = p_folder_id)
    AND (d.description IS NULL OR btrim(d.description) = '' OR btrim(d.description) = '-')
  ON CONFLICT (tenant_id, folder_id)
  DO UPDATE SET pending_count = EXCLUDED.pending_count, updated_at = now();
END $$;

CREATE OR REPLACE FUNCTION ged.on_ocr_job_status_change_refresh_metrics()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  REFRESH MATERIALIZED VIEW CONCURRENTLY ged.mv_dashboard_ocr_metrics;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_ocr_job_refresh_metrics ON ged.ocr_job;
CREATE TRIGGER trg_ocr_job_refresh_metrics
AFTER INSERT OR UPDATE OF status ON ged.ocr_job
FOR EACH STATEMENT EXECUTE FUNCTION ged.on_ocr_job_status_change_refresh_metrics();

COMMIT;
