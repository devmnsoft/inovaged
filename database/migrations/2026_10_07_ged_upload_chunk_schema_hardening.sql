-- ============================================================================
-- 2026_10_07_ged_upload_chunk_schema_hardening.sql
-- Module: GedUploadChunk
-- ----------------------------------------------------------------------------
-- Compatibilidade aditiva para instalações que receberam versões parciais do
-- schema de upload em partes. Garante somente colunas e índices consumidos pelo
-- fluxo atual, sem reescrever dados nem editar migrations já publicadas.
-- ============================================================================

CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.upload_session (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid()
);

ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS user_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS batch_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS batch_item_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS original_file_name text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS content_type text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS total_size_bytes bigint NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS chunk_size_bytes int NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS total_chunks int NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS received_chunks int NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS status text NULL DEFAULT 'OPEN';
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS temp_path text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS error_message text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS metadata_json jsonb NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS created_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS updated_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS reg_status char(1) NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.upload_session_chunk (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid()
);

ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS session_id uuid NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS chunk_index int NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS size_bytes bigint NULL DEFAULT 0;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS checksum_sha256 text NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS received_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS temp_path text NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS reg_status char(1) NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.upload_batch_item (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid()
);

ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS batch_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS upload_session_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS original_file_name text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS content_type text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS size_bytes bigint NULL DEFAULT 0;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS stored_file_name text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS checksum_sha256 text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS status text NULL DEFAULT 'PENDING';
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS started_at timestamptz NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS elapsed_ms bigint NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS attempt int NULL DEFAULT 1;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS error_message text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS error_step text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS can_retry boolean NULL DEFAULT true;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS created_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS reg_status char(1) NULL DEFAULT 'A';

UPDATE ged.upload_session
SET total_size_bytes = COALESCE(total_size_bytes, 0),
    chunk_size_bytes = COALESCE(chunk_size_bytes, 0),
    total_chunks = COALESCE(total_chunks, 0),
    received_chunks = COALESCE(received_chunks, 0),
    status = COALESCE(status, 'OPEN'),
    created_at = COALESCE(created_at, now()),
    updated_at = COALESCE(updated_at, now()),
    reg_status = COALESCE(reg_status, 'A')
WHERE total_size_bytes IS NULL
   OR chunk_size_bytes IS NULL
   OR total_chunks IS NULL
   OR received_chunks IS NULL
   OR status IS NULL
   OR created_at IS NULL
   OR updated_at IS NULL
   OR reg_status IS NULL;

UPDATE ged.upload_batch_item
SET size_bytes = COALESCE(size_bytes, 0),
    status = COALESCE(status, 'PENDING'),
    attempt = COALESCE(attempt, 1),
    can_retry = COALESCE(can_retry, true),
    created_at = COALESCE(created_at, now()),
    reg_status = COALESCE(reg_status, 'A')
WHERE size_bytes IS NULL
   OR status IS NULL
   OR attempt IS NULL
   OR can_retry IS NULL
   OR created_at IS NULL
   OR reg_status IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_upload_session_chunk_session_index
    ON ged.upload_session_chunk(session_id, chunk_index)
    WHERE session_id IS NOT NULL AND chunk_index IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_upload_session_tenant_user_status
    ON ged.upload_session(tenant_id, user_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_upload_session_batch_item
    ON ged.upload_session(tenant_id, batch_item_id)
    WHERE batch_item_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_upload_batch_item_upload_session
    ON ged.upload_batch_item(tenant_id, upload_session_id)
    WHERE upload_session_id IS NOT NULL;
