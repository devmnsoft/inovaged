-- Agendamento Automático de OCR: histórico de execuções e itens.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.ocr_auto_schedule_run (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    started_at_utc timestamptz NOT NULL,
    finished_at_utc timestamptz NULL,
    status text NOT NULL,
    candidates_found int NOT NULL DEFAULT 0,
    enqueued_count int NOT NULL DEFAULT 0,
    skipped_count int NOT NULL DEFAULT 0,
    failed_count int NOT NULL DEFAULT 0,
    message text NULL,
    correlation_id text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ged.ocr_auto_schedule_run_item (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id uuid NOT NULL REFERENCES ged.ocr_auto_schedule_run(id) ON DELETE CASCADE,
    tenant_id uuid NOT NULL,
    document_id uuid NULL,
    version_id uuid NULL,
    file_name text NULL,
    status text NOT NULL,
    reason text NULL,
    -- Mantido como texto porque instalações legadas deste projeto podem ter ged.ocr_job.id bigint,
    -- enquanto instalações consolidadas recentes usam uuid.
    ocr_job_id text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_ocr_auto_schedule_run_tenant_started
    ON ged.ocr_auto_schedule_run(tenant_id, started_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_ocr_auto_schedule_run_item_run
    ON ged.ocr_auto_schedule_run_item(run_id);

CREATE INDEX IF NOT EXISTS ix_ocr_auto_schedule_run_item_status
    ON ged.ocr_auto_schedule_run_item(status);

CREATE INDEX IF NOT EXISTS ix_ocr_auto_schedule_run_item_tenant_version
    ON ged.ocr_auto_schedule_run_item(tenant_id, version_id);
