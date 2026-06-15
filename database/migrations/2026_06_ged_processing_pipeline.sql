CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS ged.processing_job (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    document_id uuid NULL,
    document_version_id uuid NULL,
    upload_batch_id uuid NULL,
    upload_batch_item_id uuid NULL,
    job_type text NOT NULL,
    status text NOT NULL DEFAULT 'PENDING',
    priority int NOT NULL DEFAULT 5,
    attempt_count int NOT NULL DEFAULT 0,
    max_attempts int NOT NULL DEFAULT 3,
    error_message text NULL,
    started_at timestamptz NULL,
    finished_at timestamptz NULL,
    next_attempt_at timestamptz NULL,
    locked_by text NULL,
    locked_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    CONSTRAINT ck_processing_job_type CHECK (job_type IN ('PREVIEW','OCR','SMART_INDEX','QUALITY','CLASSIFICATION')),
    CONSTRAINT ck_processing_job_status CHECK (status IN ('PENDING','PROCESSING','COMPLETED','FAILED','CANCELLED'))
);

CREATE INDEX IF NOT EXISTS ix_processing_job_tenant_status_type
ON ged.processing_job (tenant_id, status, job_type);

CREATE INDEX IF NOT EXISTS ix_processing_job_tenant_version_type
ON ged.processing_job (tenant_id, document_version_id, job_type);

CREATE INDEX IF NOT EXISTS ix_processing_job_tenant_upload_batch
ON ged.processing_job (tenant_id, upload_batch_id);

CREATE INDEX IF NOT EXISTS ix_processing_job_status_next_attempt
ON ged.processing_job (status, next_attempt_at);

CREATE UNIQUE INDEX IF NOT EXISTS ux_processing_job_active_dedup_idx
ON ged.processing_job (tenant_id, (COALESCE(document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)), (COALESCE(upload_batch_item_id, '00000000-0000-0000-0000-000000000000'::uuid)), job_type)
WHERE status IN ('PENDING','PROCESSING') AND reg_status='A';

CREATE TABLE IF NOT EXISTS ged.preview_result (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    document_id uuid NULL,
    document_version_id uuid NOT NULL,
    status text NOT NULL DEFAULT 'PENDING',
    preview_path text NULL,
    thumbnail_path text NULL,
    content_type text NULL,
    size_bytes bigint NULL,
    error_message text NULL,
    generated_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_preview_result_tenant_version_active
ON ged.preview_result (tenant_id, document_version_id)
WHERE reg_status='A';

CREATE INDEX IF NOT EXISTS ix_document_tenant_folder_status_created
ON ged.document(tenant_id, folder_id, reg_status, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_document_version_tenant_document
ON ged.document_version(tenant_id, document_id);

CREATE INDEX IF NOT EXISTS ix_document_search_tenant_document
ON ged.document_search(tenant_id, document_id);

ALTER TABLE IF EXISTS ged.upload_batch_item
    ADD COLUMN IF NOT EXISTS safe_file_name text NULL,
    ADD COLUMN IF NOT EXISTS storage_path text NULL,
    ADD COLUMN IF NOT EXISTS content_hash text NULL,
    ADD COLUMN IF NOT EXISTS attempt_count int NOT NULL DEFAULT 0;

CREATE UNIQUE INDEX IF NOT EXISTS ux_upload_batch_item_file_idempotency
ON ged.upload_batch_item (tenant_id, batch_id, original_file_name, size_bytes, content_hash)
WHERE content_hash IS NOT NULL AND reg_status='A';
