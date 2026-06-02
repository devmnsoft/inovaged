CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.upload_batch (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    folder_id uuid NULL,
    requested_folder_id uuid NULL,
    created_by uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz NULL,
    finished_at timestamptz NULL,
    status text NOT NULL DEFAULT 'OPEN',
    total_files int NOT NULL DEFAULT 0,
    success_files int NOT NULL DEFAULT 0,
    failed_files int NOT NULL DEFAULT 0,
    skipped_files int NOT NULL DEFAULT 0,
    source_ip text NULL,
    user_agent text NULL,
    correlation_id text NULL,
    error_message text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    CONSTRAINT ck_upload_batch_status CHECK (status IN ('OPEN','PROCESSING','COMPLETED','PARTIAL_ERROR','ERROR','CANCELLED'))
);

ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS started_at timestamptz NULL;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS error_message text NULL;

CREATE TABLE IF NOT EXISTS ged.upload_batch_item (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    batch_id uuid NOT NULL REFERENCES ged.upload_batch(id) ON DELETE CASCADE,
    folder_id uuid NULL,
    requested_folder_id uuid NULL,
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

ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;

CREATE TABLE IF NOT EXISTS ged.folder_virtual_map (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    virtual_folder_id uuid NOT NULL,
    real_folder_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);

CREATE INDEX IF NOT EXISTS ix_upload_batch_tenant_created
ON ged.upload_batch(tenant_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_upload_batch_tenant_status
ON ged.upload_batch(tenant_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_upload_batch_created_by
ON ged.upload_batch(tenant_id, created_by, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_upload_batch_tenant_folder_created
ON ged.upload_batch(tenant_id, folder_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_upload_batch_item_batch
ON ged.upload_batch_item(tenant_id, batch_id);

CREATE INDEX IF NOT EXISTS ix_upload_batch_item_status
ON ged.upload_batch_item(tenant_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_upload_batch_item_folder
ON ged.upload_batch_item(tenant_id, folder_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_upload_batch_item_checksum
ON ged.upload_batch_item(tenant_id, folder_id, checksum_sha256)
WHERE checksum_sha256 IS NOT NULL AND reg_status = 'A';

CREATE UNIQUE INDEX IF NOT EXISTS ux_folder_virtual_map_active
ON ged.folder_virtual_map(tenant_id, virtual_folder_id)
WHERE reg_status = 'A';

CREATE INDEX IF NOT EXISTS ix_folder_virtual_map_real
ON ged.folder_virtual_map(tenant_id, real_folder_id)
WHERE reg_status = 'A';
