-- Agendamento Automático de OCR: histórico de execuções e itens.
-- Idempotente, seguro para bancos existentes e compatível com execução repetida.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.ocr_auto_schedule_run (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    started_at_utc timestamptz NOT NULL DEFAULT now(),
    finished_at_utc timestamptz NULL,
    status text NOT NULL DEFAULT 'STARTED',
    candidates_found int NOT NULL DEFAULT 0,
    enqueued_count int NOT NULL DEFAULT 0,
    skipped_count int NOT NULL DEFAULT 0,
    failed_count int NOT NULL DEFAULT 0,
    message text NULL,
    correlation_id text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS started_at_utc timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS finished_at_utc timestamptz NULL;
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'STARTED';
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS candidates_found int NOT NULL DEFAULT 0;
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS enqueued_count int NOT NULL DEFAULT 0;
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS skipped_count int NOT NULL DEFAULT 0;
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS failed_count int NOT NULL DEFAULT 0;
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS message text NULL;
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.ocr_auto_schedule_run ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS ged.ocr_auto_schedule_run_item (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NULL,
    version_id uuid NULL,
    file_name text NULL,
    status text NOT NULL DEFAULT 'PENDING',
    reason text NULL,
    ocr_job_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS run_id uuid;
ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS file_name text NULL;
ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'PENDING';
ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS reason text NULL;
ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS ocr_job_id uuid NULL;
ALTER TABLE ged.ocr_auto_schedule_run_item ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

CREATE INDEX IF NOT EXISTS ix_ocr_auto_schedule_run_tenant_started
ON ged.ocr_auto_schedule_run(tenant_id, started_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_ocr_auto_schedule_run_status
ON ged.ocr_auto_schedule_run(tenant_id, status);

CREATE INDEX IF NOT EXISTS ix_ocr_auto_schedule_run_item_run
ON ged.ocr_auto_schedule_run_item(run_id);

CREATE INDEX IF NOT EXISTS ix_ocr_auto_schedule_run_item_status
ON ged.ocr_auto_schedule_run_item(tenant_id, status);
