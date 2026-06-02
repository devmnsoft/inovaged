CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.upload_batch (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    folder_id uuid NULL,
    created_by uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    finished_at timestamptz NULL,
    status text NOT NULL DEFAULT 'OPEN',
    total_files int NOT NULL DEFAULT 0,
    success_files int NOT NULL DEFAULT 0,
    failed_files int NOT NULL DEFAULT 0,
    skipped_files int NOT NULL DEFAULT 0,
    source_ip text NULL,
    user_agent text NULL,
    correlation_id text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    CONSTRAINT ck_upload_batch_status CHECK (status IN ('OPEN','PROCESSING','COMPLETED','PARTIAL_ERROR','ERROR','CANCELLED'))
);

CREATE TABLE IF NOT EXISTS ged.upload_batch_item (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    batch_id uuid NOT NULL REFERENCES ged.upload_batch(id),
    folder_id uuid NULL,
    document_id uuid NULL,
    version_id uuid NULL,
    original_file_name text NOT NULL,
    stored_file_name text NULL,
    content_type text NULL,
    size_bytes bigint NULL,
    status text NOT NULL DEFAULT 'PENDING',
    error_message text NULL,
    error_step text NULL,
    can_retry boolean NOT NULL DEFAULT true,
    checksum_sha256 text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz NULL,
    finished_at timestamptz NULL,
    elapsed_ms bigint NULL,
    attempt int NOT NULL DEFAULT 0,
    correlation_id text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    CONSTRAINT ck_upload_batch_item_status CHECK (status IN ('PENDING','RECEIVING','SAVED','DOCUMENT_CREATED','OCR_QUEUED','PREVIEW_QUEUED','COMPLETED','ERROR','SKIPPED','CANCELLED'))
);

CREATE INDEX IF NOT EXISTS ix_upload_batch_item_tenant_batch ON ged.upload_batch_item (tenant_id, batch_id);
CREATE INDEX IF NOT EXISTS ix_upload_batch_item_tenant_status ON ged.upload_batch_item (tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_upload_batch_tenant_created ON ged.upload_batch (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_batch_tenant_folder_created ON ged.upload_batch (tenant_id, folder_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_batch_item_checksum_scope ON ged.upload_batch_item (checksum_sha256, tenant_id, folder_id) WHERE checksum_sha256 IS NOT NULL;
