-- InovaGED - Consolidação idempotente de schema GED/OCR/upload/logs/versionamento.
-- Pode ser executado repetidas vezes. Não apaga dados e não sobrescreve registros reais.

-- Histórico de migrations / schema base
CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- GED base: tipos usados por documentos, OCR e preview.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'ged' AND t.typname = 'document_status_enum') THEN
        CREATE TYPE ged.document_status_enum AS ENUM ('DRAFT','ACTIVE','ARCHIVED','DELETED');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'ged' AND t.typname = 'document_visibility_enum') THEN
        CREATE TYPE ged.document_visibility_enum AS ENUM ('INTERNAL','RESTRICTED','PUBLIC');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'ged' AND t.typname = 'ocr_status_enum') THEN
        CREATE TYPE ged.ocr_status_enum AS ENUM ('PENDING','PROCESSING','COMPLETED','ERROR');
    END IF;

    IF EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'ged' AND t.typname = 'ocr_status_enum') THEN
        IF NOT EXISTS (SELECT 1 FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='ged' AND t.typname='ocr_status_enum' AND e.enumlabel='DONE') THEN
            ALTER TYPE ged.ocr_status_enum ADD VALUE 'DONE';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='ged' AND t.typname='ocr_status_enum' AND e.enumlabel='FAILED') THEN
            ALTER TYPE ged.ocr_status_enum ADD VALUE 'FAILED';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='ged' AND t.typname='ocr_status_enum' AND e.enumlabel='CANCELED') THEN
            ALTER TYPE ged.ocr_status_enum ADD VALUE 'CANCELED';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='ged' AND t.typname='ocr_status_enum' AND e.enumlabel='CANCELLED') THEN
            ALTER TYPE ged.ocr_status_enum ADD VALUE 'CANCELLED';
        END IF;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'ged' AND t.typname = 'preview_processing_status') THEN
        CREATE TYPE ged.preview_processing_status AS ENUM ('PENDING','PROCESSING','READY','FAILED','CANCELED');
    END IF;
END $$;

-- GED base: pastas e documentos.
CREATE TABLE IF NOT EXISTS ged.folder (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    parent_id uuid NULL,
    name text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);

CREATE TABLE IF NOT EXISTS ged.document (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    folder_id uuid NULL,
    title text NULL,
    current_version_id uuid NULL,
    status text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS current_version_id uuid NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS status text NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS created_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS reg_status char(1) NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.document_version (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    document_id uuid NULL,
    file_name text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_at_utc timestamptz NULL,
    uploaded_at_utc timestamptz NULL,
    is_current boolean NULL,
    is_partial_document boolean NOT NULL DEFAULT false,
    consolidated_version_id uuid NULL,
    partial_group_id uuid NULL,
    partial_part_number int NULL,
    partial_total_parts int NULL,
    partial_status text NOT NULL DEFAULT 'NOT_PARTIAL',
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS file_name text NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS created_at timestamptz NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS created_at_utc timestamptz NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS uploaded_at_utc timestamptz NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS is_current boolean NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS is_partial_document boolean NULL DEFAULT false;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS consolidated_version_id uuid NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS partial_group_id uuid NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS partial_part_number int NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS partial_total_parts int NULL;
ALTER TABLE ged.document_version ADD COLUMN IF NOT EXISTS partial_status text NULL DEFAULT 'NOT_PARTIAL';

UPDATE ged.document_version
SET partial_status = 'NOT_PARTIAL'
WHERE partial_status IS NULL OR partial_status = '';
UPDATE ged.document_version
SET is_partial_document = false
WHERE is_partial_document IS NULL;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='created_at_utc')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='created_at') THEN
        UPDATE ged.document_version SET uploaded_at_utc = COALESCE(uploaded_at_utc, created_at_utc, created_at, now()) WHERE uploaded_at_utc IS NULL;
    ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='created_at_utc') THEN
        UPDATE ged.document_version SET uploaded_at_utc = COALESCE(uploaded_at_utc, created_at_utc, now()) WHERE uploaded_at_utc IS NULL;
    ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='created_at') THEN
        UPDATE ged.document_version SET uploaded_at_utc = COALESCE(uploaded_at_utc, created_at, now()) WHERE uploaded_at_utc IS NULL;
    ELSE
        UPDATE ged.document_version SET uploaded_at_utc = COALESCE(uploaded_at_utc, now()) WHERE uploaded_at_utc IS NULL;
    END IF;
END $$;

-- Documento parcial: controle de partes/fracionamento e consolidação.
CREATE TABLE IF NOT EXISTS ged.document_partial_part (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
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
    notes text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.document_partial_part ADD COLUMN IF NOT EXISTS notes text NULL;

-- Upload batch: lote e itens de envio múltiplo.
CREATE TABLE IF NOT EXISTS ged.upload_batch (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NULL,
    requested_folder_id uuid NULL,
    folder_id uuid NULL,
    status text NOT NULL DEFAULT 'PENDING',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS status text NULL DEFAULT 'PENDING';
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;
ALTER TABLE ged.upload_batch ADD COLUMN IF NOT EXISTS created_at timestamptz NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS ged.upload_batch_item (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    batch_id uuid NULL,
    upload_session_id uuid NULL,
    document_id uuid NULL,
    version_id uuid NULL,
    original_file_name text NULL,
    status text NOT NULL DEFAULT 'PENDING',
    error_message text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS upload_session_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS batch_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS status text NULL DEFAULT 'PENDING';
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS created_at timestamptz NULL DEFAULT now();

-- Upload chunked: sessões e partes de arquivos grandes.
CREATE TABLE IF NOT EXISTS ged.upload_session (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    folder_id uuid NOT NULL,
    requested_folder_id uuid NULL,
    batch_id uuid NULL,
    original_file_name text NOT NULL,
    content_type text NULL,
    total_size_bytes bigint NOT NULL DEFAULT 0,
    chunk_size_bytes int NOT NULL DEFAULT 0,
    total_chunks int NOT NULL DEFAULT 0,
    received_chunks int NOT NULL DEFAULT 0,
    status text NOT NULL DEFAULT 'STARTED',
    temp_path text NULL,
    document_id uuid NULL,
    version_id uuid NULL,
    error_message text NULL,
    correlation_id text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS user_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS requested_folder_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS batch_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS original_file_name text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS content_type text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS total_size_bytes bigint NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS chunk_size_bytes int NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS total_chunks int NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS received_chunks int NULL DEFAULT 0;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS status text NULL DEFAULT 'STARTED';
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS temp_path text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS error_message text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS created_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS updated_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS reg_status char(1) NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.upload_session_chunk (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id uuid NOT NULL,
    chunk_index int NOT NULL,
    size_bytes bigint NOT NULL DEFAULT 0,
    checksum_sha256 text NULL,
    temp_path text NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A',
    UNIQUE(session_id, chunk_index)
);
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS session_id uuid NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS chunk_index int NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS size_bytes bigint NULL DEFAULT 0;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS checksum_sha256 text NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS temp_path text NULL;
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS received_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.upload_session_chunk ADD COLUMN IF NOT EXISTS reg_status char(1) NULL DEFAULT 'A';

-- OCR: índice textual e fila de processamento.
CREATE TABLE IF NOT EXISTS ged.document_search (
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_id uuid NOT NULL,
    document_version_id uuid NULL,
    file_name text NULL,
    ocr_text text NULL,
    search_vector tsvector NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE ged.document_search ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.document_search ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.document_search ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.document_search ADD COLUMN IF NOT EXISTS ocr_text text NULL;

CREATE TABLE IF NOT EXISTS ged.ocr_job (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    document_version_id uuid NULL,
    status ged.ocr_status_enum NOT NULL DEFAULT 'PENDING',
    requested_at timestamptz NOT NULL DEFAULT now(),
    finished_at timestamptz NULL,
    invalidate_digital_signatures boolean NOT NULL DEFAULT false,
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS status ged.ocr_status_enum NULL DEFAULT 'PENDING';
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS requested_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS invalidate_digital_signatures boolean NULL DEFAULT false;

-- Auditoria: tabela legada com fallback para SystemLogs.
CREATE TABLE IF NOT EXISTS ged.audit_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    user_id uuid NULL,
    user_name text NULL,
    action text NULL,
    event_type text NULL,
    source text NULL,
    entity_name text NULL,
    entity_id text NULL,
    message text NULL,
    details text NULL,
    correlation_id text NULL,
    ip_address text NULL,
    user_agent text NULL,
    created_at timestamptz NULL,
    reg_date timestamptz NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS created_at timestamptz NULL;
ALTER TABLE ged.audit_log ADD COLUMN IF NOT EXISTS user_name text NULL;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='audit_log' AND column_name='reg_date') THEN
        UPDATE ged.audit_log SET created_at = COALESCE(created_at, reg_date, now()) WHERE created_at IS NULL;
    ELSE
        UPDATE ged.audit_log SET created_at = COALESCE(created_at, now()) WHERE created_at IS NULL;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='audit_log' AND column_name='user_id') THEN
        UPDATE ged.audit_log SET user_name = COALESCE(user_name, user_id::text, 'Sistema') WHERE user_name IS NULL;
    ELSE
        UPDATE ged.audit_log SET user_name = COALESCE(user_name, 'Sistema') WHERE user_name IS NULL;
    END IF;
END $$;

-- Auditoria: tabela padrão da aplicação para SystemLogs.
CREATE TABLE IF NOT EXISTS ged.app_audit_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    user_id uuid NULL,
    user_name text NULL,
    action text NOT NULL,
    event_type text NOT NULL DEFAULT 'INFO',
    source text NULL,
    entity_name text NULL,
    entity_id text NULL,
    method text NULL,
    path text NULL,
    status_code int NULL,
    message text NULL,
    details jsonb NULL,
    correlation_id text NULL,
    ip_address text NULL,
    user_agent text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS created_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS user_name text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS action text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS event_type text NULL DEFAULT 'INFO';
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS source text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS entity_name text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS entity_id text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS method text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS path text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS status_code int NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS message text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS details jsonb NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS ip_address text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS user_agent text NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS reg_status char(1) NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.folder_virtual_map (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    virtual_folder_id uuid NOT NULL,
    real_folder_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);

CREATE TABLE IF NOT EXISTS ged.app_user (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    user_name text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);

-- Histórico de migrations: registro idempotente dos scripts aplicados.
CREATE TABLE IF NOT EXISTS ged.schema_migration_history (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    script_name text NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now(),
    applied_by text NULL,
    checksum_sha256 text NULL,
    success boolean NOT NULL DEFAULT true,
    notes text NULL
);


-- Backfills seguros: preenchem somente valores nulos para manter compatibilidade operacional, sem apagar ou substituir dados reais.
UPDATE ged.document SET created_at = COALESCE(created_at, now()) WHERE created_at IS NULL;
UPDATE ged.document SET reg_status = COALESCE(reg_status, 'A') WHERE reg_status IS NULL;
UPDATE ged.upload_batch SET status = COALESCE(status, 'PENDING'), created_at = COALESCE(created_at, now()) WHERE status IS NULL OR created_at IS NULL;
UPDATE ged.upload_batch_item SET status = COALESCE(status, 'PENDING'), created_at = COALESCE(created_at, now()) WHERE status IS NULL OR created_at IS NULL;
UPDATE ged.upload_session
SET total_size_bytes = COALESCE(total_size_bytes, 0),
    chunk_size_bytes = COALESCE(chunk_size_bytes, 0),
    total_chunks = COALESCE(total_chunks, 0),
    received_chunks = COALESCE(received_chunks, 0),
    status = COALESCE(status, 'STARTED'),
    created_at = COALESCE(created_at, now()),
    updated_at = COALESCE(updated_at, now()),
    reg_status = COALESCE(reg_status, 'A')
WHERE total_size_bytes IS NULL OR chunk_size_bytes IS NULL OR total_chunks IS NULL OR received_chunks IS NULL OR status IS NULL OR created_at IS NULL OR updated_at IS NULL OR reg_status IS NULL;
UPDATE ged.upload_session_chunk
SET size_bytes = COALESCE(size_bytes, 0),
    received_at = COALESCE(received_at, now()),
    reg_status = COALESCE(reg_status, 'A')
WHERE size_bytes IS NULL OR received_at IS NULL OR reg_status IS NULL;
UPDATE ged.ocr_job
SET status = COALESCE(status, 'PENDING'),
    requested_at = COALESCE(requested_at, now()),
    invalidate_digital_signatures = COALESCE(invalidate_digital_signatures, false)
WHERE status IS NULL OR requested_at IS NULL OR invalidate_digital_signatures IS NULL;
UPDATE ged.app_audit_log
SET created_at = COALESCE(created_at, now()),
    event_type = COALESCE(event_type, 'INFO'),
    reg_status = COALESCE(reg_status, 'A')
WHERE created_at IS NULL OR event_type IS NULL OR reg_status IS NULL;

-- Índices de auditoria e logs.
CREATE INDEX IF NOT EXISTS ix_app_audit_log_tenant_created ON ged.app_audit_log(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_app_audit_log_action_created ON ged.app_audit_log(action, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_app_audit_log_user_created ON ged.app_audit_log(user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_app_audit_log_correlation ON ged.app_audit_log(correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_audit_log_tenant_created ON ged.audit_log(tenant_id, created_at DESC);

-- Índices de upload/documento parcial.
CREATE INDEX IF NOT EXISTS ix_document_partial_part_tenant_document ON ged.document_partial_part(tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_group_part ON ged.document_partial_part(tenant_id, partial_group_id, part_number);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_status ON ged.document_partial_part(tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_upload_batch_tenant_status ON ged.upload_batch(tenant_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_batch_item_status ON ged.upload_batch_item(tenant_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_batch_item_upload_session ON ged.upload_batch_item(upload_session_id) WHERE upload_session_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_upload_session_tenant_user_status ON ged.upload_session(tenant_id, user_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_session_status ON ged.upload_session(status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_upload_session_chunk_session ON ged.upload_session_chunk(session_id, chunk_index);
CREATE UNIQUE INDEX IF NOT EXISTS ux_schema_migration_history_script ON ged.schema_migration_history(script_name);

-- Índices com validação de colunas opcionais/ambientes heterogêneos.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='folder_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='status') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_tenant_folder_status ON ged.document(tenant_id, folder_id, status)';
    ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='folder_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='reg_status') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_tenant_folder_reg_status ON ged.document(tenant_id, folder_id, reg_status)';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='document_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='is_current') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_version_document_current ON ged.document_version(document_id, is_current)';
    ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='current_version_id') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_current_version ON ged.document(current_version_id) WHERE current_version_id IS NOT NULL';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='partial_group_id') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_version_partial_group_id ON ged.document_version(partial_group_id) WHERE partial_group_id IS NOT NULL';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='partial_status') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_version_partial_status ON ged.document_version(partial_status)';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_version' AND column_name='uploaded_at_utc') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_version_uploaded_at_utc ON ged.document_version(uploaded_at_utc DESC)';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='document_version_id') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ged_document_search_tenant_version ON ged.document_search(tenant_id, document_version_id)';
    ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='version_id') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ged_document_search_tenant_version ON ged.document_search(tenant_id, version_id)';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='ocr_job' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='ocr_job' AND column_name='document_version_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='ocr_job' AND column_name='status') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job(tenant_id, document_version_id, status)';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='document_type_id') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_tenant_document_type ON ged.document(tenant_id, document_type_id) WHERE document_type_id IS NOT NULL';
    END IF;
END $$;

INSERT INTO ged.schema_migration_history(script_name, notes)
VALUES ('2026_06_ged_schema_consolidation.sql', 'Consolidação de schema GED/OCR/upload/logs/versionamento')
ON CONFLICT (script_name) DO UPDATE
SET applied_at = now(),
    success = true,
    notes = EXCLUDED.notes;

INSERT INTO ged.schema_migration_history(script_name, notes)
VALUES (
    'database/apply_all_required_migrations.sql',
    'Schema consolidado GED/OCR/upload/logs/documentos parciais'
)
ON CONFLICT (script_name) DO UPDATE
SET applied_at = now(),
    success = true,
    notes = EXCLUDED.notes;
