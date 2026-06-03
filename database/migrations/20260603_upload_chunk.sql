CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.upload_session (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    batch_id uuid NULL,
    batch_item_id uuid NULL,
    folder_id uuid NOT NULL,
    requested_folder_id uuid NULL,
    original_file_name text NOT NULL,
    content_type text NULL,
    total_size_bytes bigint NOT NULL,
    chunk_size_bytes int NOT NULL,
    total_chunks int NOT NULL,
    received_chunks int NOT NULL DEFAULT 0,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    temp_path text NOT NULL,
    document_id uuid NULL,
    version_id uuid NULL,
    error_message text NULL,
    metadata_json jsonb NULL,
    correlation_id text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    CONSTRAINT ck_upload_session_status CHECK (status IN ('OPEN','RECEIVING','COMPLETING','COMPLETED','ERROR','CANCELLED'))
);

CREATE TABLE IF NOT EXISTS ged.upload_session_chunk (
    id uuid PRIMARY KEY,
    session_id uuid NOT NULL REFERENCES ged.upload_session(id) ON DELETE CASCADE,
    chunk_index int NOT NULL,
    size_bytes bigint NOT NULL,
    checksum_sha256 text NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    temp_path text NOT NULL,
    CONSTRAINT ux_upload_session_chunk UNIQUE(session_id, chunk_index)
);

ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS upload_session_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS progress numeric(6,2) NULL;

CREATE INDEX IF NOT EXISTS ix_upload_session_tenant_user_status ON ged.upload_session(tenant_id, user_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_session_batch ON ged.upload_session(tenant_id, batch_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_session_chunk_session ON ged.upload_session_chunk(session_id, chunk_index);
CREATE INDEX IF NOT EXISTS ix_upload_batch_item_upload_session ON ged.upload_batch_item(tenant_id, upload_session_id) WHERE upload_session_id IS NOT NULL;
