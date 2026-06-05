-- InovaGED GED schema consolidation (idempotent)
-- Consolida estruturas críticas esperadas pelo código atual: versionamento, uploads, OCR, documentos parciais e logs.

CREATE SCHEMA IF NOT EXISTS ged;

DO $$
BEGIN
    CREATE TYPE ged.ocr_status_enum AS ENUM ('PENDING','PROCESSING','COMPLETED','ERROR','CANCELLED');
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$
BEGIN
    CREATE TYPE ged.preview_processing_status AS ENUM ('PENDING','PROCESSING','READY','ERROR');
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- -----------------------------------------------------------------------------
-- document_version: campos de upload, parcialidade e consolidação.
-- A tabela real usada pelo projeto é ged.document_version.
-- -----------------------------------------------------------------------------
DO $$
DECLARE
    backfill_expr text := 'now()';
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='ged' AND table_name='document_version') THEN
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS uploaded_at_utc timestamptz NULL;
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS is_partial_document boolean NOT NULL DEFAULT false;
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS partial_group_id uuid NULL;
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS partial_part_number int NULL;
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS partial_total_parts int NULL;
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS partial_status text NOT NULL DEFAULT 'NOT_PARTIAL';
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS consolidated_version_id uuid NULL;

        -- Compatibilidade com versões anteriores do código que usavam nomes legados.
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS is_document_incomplete boolean NOT NULL DEFAULT false;
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS part_number int NULL;
        ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS total_parts int NULL;

        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='created_at_utc') THEN
            backfill_expr := 'created_at_utc';
        ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='created_at') THEN
            backfill_expr := 'created_at';
        ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='reg_date') THEN
            backfill_expr := 'reg_date';
        END IF;

        EXECUTE format('UPDATE ged.document_version SET uploaded_at_utc = COALESCE(uploaded_at_utc, %s, now()) WHERE uploaded_at_utc IS NULL', backfill_expr);
        UPDATE ged.document_version
           SET is_partial_document = COALESCE(is_partial_document, false),
               is_document_incomplete = COALESCE(is_document_incomplete, false),
               partial_status = COALESCE(NULLIF(partial_status, ''), CASE WHEN COALESCE(is_partial_document, false) THEN 'UPLOADED' ELSE 'NOT_PARTIAL' END),
               partial_part_number = COALESCE(partial_part_number, part_number),
               partial_total_parts = COALESCE(partial_total_parts, total_parts),
               part_number = COALESCE(part_number, partial_part_number),
               total_parts = COALESCE(total_parts, partial_total_parts)
         WHERE uploaded_at_utc IS NULL
            OR partial_status IS NULL
            OR partial_status = ''
            OR partial_part_number IS NULL
            OR partial_total_parts IS NULL
            OR part_number IS NULL
            OR total_parts IS NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='ged' AND table_name='document_version') THEN
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='partial_group_id') THEN
            CREATE INDEX IF NOT EXISTS ix_document_version_partial_group_id ON ged.document_version (tenant_id, partial_group_id) WHERE partial_group_id IS NOT NULL;
        END IF;
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='partial_status') THEN
            CREATE INDEX IF NOT EXISTS ix_document_version_partial_status ON ged.document_version (tenant_id, partial_status);
        END IF;
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='uploaded_at_utc') THEN
            CREATE INDEX IF NOT EXISTS ix_document_version_uploaded_at_utc ON ged.document_version (tenant_id, uploaded_at_utc DESC);
        END IF;
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='is_current') THEN
            CREATE INDEX IF NOT EXISTS ix_document_version_document_current ON ged.document_version (document_id, is_current);
        END IF;
    END IF;
END $$;

-- -----------------------------------------------------------------------------
-- Upload em lote.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ged.upload_batch (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    folder_id uuid NULL,
    requested_folder_id uuid NULL,
    created_by uuid NULL,
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
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'OPEN';
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS total_files int NOT NULL DEFAULT 0;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS success_files int NOT NULL DEFAULT 0;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS failed_files int NOT NULL DEFAULT 0;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS skipped_files int NOT NULL DEFAULT 0;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS error_message text NULL;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;

CREATE TABLE IF NOT EXISTS ged.upload_batch_item (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    batch_id uuid NOT NULL REFERENCES ged.upload_batch(id) ON DELETE CASCADE,
    folder_id uuid NULL,
    requested_folder_id uuid NULL,
    document_id uuid NULL,
    version_id uuid NULL,
    upload_session_id uuid NULL,
    original_file_name text NOT NULL,
    stored_file_name text NULL,
    content_type text NULL,
    size_bytes bigint NULL,
    status text NOT NULL DEFAULT 'PENDING',
    error_message text NULL,
    error_step text NULL,
    can_retry boolean NOT NULL DEFAULT true,
    checksum_sha256 text NULL,
    checksum text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz NULL,
    finished_at timestamptz NULL,
    elapsed_ms bigint NULL,
    attempt int NOT NULL DEFAULT 0,
    progress numeric(6,2) NULL,
    correlation_id text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS upload_session_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS original_file_name text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'PENDING';
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS error_message text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS checksum_sha256 text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS checksum text NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS elapsed_ms bigint NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS attempt int NOT NULL DEFAULT 0;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS progress numeric(6,2) NULL;

-- -----------------------------------------------------------------------------
-- Upload em chunks.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ged.upload_session (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    batch_id uuid NULL,
    batch_item_id uuid NULL,
    folder_id uuid NULL,
    requested_folder_id uuid NULL,
    original_file_name text NOT NULL,
    content_type text NULL,
    total_size_bytes bigint NOT NULL,
    chunk_size_bytes int NOT NULL,
    total_chunks int NOT NULL,
    received_chunks int NOT NULL DEFAULT 0,
    status text NOT NULL DEFAULT 'OPEN',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    temp_path text NULL,
    document_id uuid NULL,
    version_id uuid NULL,
    error_message text NULL,
    metadata_json jsonb NULL,
    correlation_id text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS user_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS original_file_name text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS total_size_bytes bigint NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS chunk_size_bytes int NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS total_chunks int NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS received_chunks int NOT NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'OPEN';
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS temp_path text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS error_message text NULL;

CREATE TABLE IF NOT EXISTS ged.upload_session_chunk (
    id uuid PRIMARY KEY,
    session_id uuid NOT NULL REFERENCES ged.upload_session(id) ON DELETE CASCADE,
    chunk_index int NOT NULL,
    size_bytes bigint NOT NULL,
    checksum_sha256 text NULL,
    checksum text NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    temp_path text NULL
);
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS session_id uuid NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS chunk_index int NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS size_bytes bigint NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS checksum_sha256 text NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS checksum text NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS temp_path text NULL;

-- -----------------------------------------------------------------------------
-- Controle de documentos parciais.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ged.document_partial_part (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_id uuid NOT NULL,
    partial_group_id uuid NOT NULL,
    part_number int NOT NULL,
    total_parts int NULL,
    file_name text NULL,
    size_bytes bigint NULL,
    uploaded_at_utc timestamptz NOT NULL DEFAULT now(),
    uploaded_by uuid NULL,
    status text NOT NULL DEFAULT 'UPLOADED',
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);

CREATE TABLE IF NOT EXISTS ged.document_part (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_id uuid NOT NULL,
    part_number int NULL,
    total_parts int NULL,
    uploaded_at_utc timestamptz NOT NULL DEFAULT now(),
    is_consolidated boolean NOT NULL DEFAULT false,
    consolidated_at_utc timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- Mapeamento de pasta virtual.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ged.folder_virtual_map (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    virtual_folder_id uuid NOT NULL,
    real_folder_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);

-- -----------------------------------------------------------------------------
-- OCR e busca: estrutura mínima compatível com consultas atuais.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ged.ocr_job (
    id bigserial PRIMARY KEY,
    tenant_id uuid NOT NULL,
    document_version_id uuid NOT NULL,
    status ged.ocr_status_enum NOT NULL DEFAULT 'PENDING',
    requested_by uuid NULL,
    requested_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz NULL,
    finished_at timestamptz NULL,
    error_message text NULL,
    invalidate_digital_signatures boolean NOT NULL DEFAULT false,
    lease_expires_at timestamptz NULL,
    attempts int NOT NULL DEFAULT 0,
    last_attempt_at timestamptz NULL,
    next_attempt_at timestamptz NULL,
    cancel_requested boolean NOT NULL DEFAULT false,
    cancel_requested_at timestamptz NULL,
    cancelled_by uuid NULL,
    cancel_reason text NULL
);
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS status ged.ocr_status_enum NOT NULL DEFAULT 'PENDING';
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS requested_by uuid NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS requested_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS started_at timestamptz NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS error_message text NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS invalidate_digital_signatures boolean NOT NULL DEFAULT false;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS lease_expires_at timestamptz NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS attempts int NOT NULL DEFAULT 0;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS last_attempt_at timestamptz NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS cancel_requested boolean NOT NULL DEFAULT false;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS cancel_requested_at timestamptz NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS cancelled_by uuid NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS cancel_reason text NULL;

CREATE TABLE IF NOT EXISTS ged.document_search (
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_id uuid NOT NULL,
    file_name text NULL,
    ocr_text text NULL,
    search_vector tsvector NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, document_id, version_id)
);
ALTER TABLE ged.document_search ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.document_search ADD COLUMN IF NOT EXISTS file_name text NULL;
ALTER TABLE ged.document_search ADD COLUMN IF NOT EXISTS ocr_text text NULL;
ALTER TABLE ged.document_search ADD COLUMN IF NOT EXISTS search_vector tsvector NULL;

-- -----------------------------------------------------------------------------
-- SystemLogs: compatibilidade mínima quando a tabela existir/criada.
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS ged.audit_log (
    id uuid PRIMARY KEY,
    tenant_id uuid NULL,
    user_id uuid NULL,
    user_name text NULL,
    event_type text NULL,
    action text NULL,
    source text NULL,
    entity_name text NULL,
    entity_id text NULL,
    message text NULL,
    details text NULL,
    path text NULL,
    http_method text NULL,
    http_status int NULL,
    ip_address text NULL,
    user_agent text NULL,
    correlation_id text NULL,
    metadata_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS user_name text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS event_type text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS action text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS source text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS entity_name text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS entity_id text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS message text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS details text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS path text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS http_method text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS http_status int NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS metadata_json jsonb NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS ged.app_audit_log (
    id uuid PRIMARY KEY,
    tenant_id uuid NULL,
    user_id uuid NULL,
    user_name text NULL,
    action text NULL,
    entity_type text NULL,
    entity_id text NULL,
    message text NULL,
    ip_address text NULL,
    user_agent text NULL,
    correlation_id text NULL,
    metadata_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS user_name text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS metadata_json jsonb NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

-- -----------------------------------------------------------------------------
-- Índices críticos.
-- -----------------------------------------------------------------------------
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='ged' AND table_name='document') THEN
        CREATE INDEX IF NOT EXISTS ix_document_tenant_folder_status ON ged.document (tenant_id, folder_id, status);
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_upload_batch_tenant_created ON ged.upload_batch(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_batch_tenant_status ON ged.upload_batch(tenant_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_batch_item_batch ON ged.upload_batch_item(tenant_id, batch_id);
CREATE INDEX IF NOT EXISTS ix_upload_batch_item_status ON ged.upload_batch_item(tenant_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_batch_item_upload_session ON ged.upload_batch_item(tenant_id, upload_session_id) WHERE upload_session_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_upload_session_tenant_user_status ON ged.upload_session(tenant_id, user_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_session_batch ON ged.upload_session(tenant_id, batch_id, created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_upload_session_chunk_session_index ON ged.upload_session_chunk(session_id, chunk_index);
CREATE INDEX IF NOT EXISTS ix_upload_session_chunk_session ON ged.upload_session_chunk(session_id, chunk_index);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_document ON ged.document_partial_part (tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_group_part ON ged.document_partial_part (tenant_id, partial_group_id, part_number);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_status ON ged.document_partial_part (tenant_id, status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_folder_virtual_map_active ON ged.folder_virtual_map(tenant_id, virtual_folder_id) WHERE reg_status = 'A';
CREATE INDEX IF NOT EXISTS ix_folder_virtual_map_real ON ged.folder_virtual_map(tenant_id, real_folder_id) WHERE reg_status = 'A';
CREATE INDEX IF NOT EXISTS ix_audit_log_tenant_created ON ged.audit_log (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job (tenant_id, document_version_id, status);
CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_status_requested_at ON ged.ocr_job (tenant_id, status, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_status_next_attempt ON ged.ocr_job(tenant_id, status, next_attempt_at, requested_at);
CREATE INDEX IF NOT EXISTS ix_ged_document_search_tenant_version ON ged.document_search(tenant_id, version_id);
-- Índice trigram pode ser aplicado em ambientes com pg_trgm habilitado, se necessário.
