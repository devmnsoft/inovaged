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
    updated_at timestamptz NULL,
    updated_by uuid NULL,
    deleted_at timestamptz NULL,
    deleted_by uuid NULL,
    deleted_reason text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS current_version_id uuid NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS status text NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS created_at timestamptz NULL DEFAULT now();
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS reg_status char(1) NULL DEFAULT 'A';

-- GED document soft delete: campos próprios de exclusão lógica.
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS deleted_at timestamptz NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS deleted_by uuid NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS deleted_reason text NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS updated_at timestamptz NULL;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS updated_by uuid NULL;
CREATE INDEX IF NOT EXISTS ix_document_tenant_reg_status ON ged.document(tenant_id, reg_status);
CREATE INDEX IF NOT EXISTS ix_document_tenant_folder_reg_status ON ged.document(tenant_id, folder_id, reg_status);
CREATE INDEX IF NOT EXISTS ix_document_deleted_at ON ged.document(deleted_at) WHERE deleted_at IS NOT NULL;

CREATE TABLE IF NOT EXISTS ged.document_version (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    document_id uuid NULL,
    file_name text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_at_utc timestamptz NULL,
    uploaded_at_utc timestamptz NULL,
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
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ux_document_partial_part_version'
          AND conrelid = 'ged.document_partial_part'::regclass
    ) THEN
        ALTER TABLE ged.document_partial_part
            ADD CONSTRAINT ux_document_partial_part_version
            UNIQUE (tenant_id, version_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ux_document_partial_part_group_part_active'
          AND conrelid = 'ged.document_partial_part'::regclass
    ) THEN
        ALTER TABLE ged.document_partial_part
            ADD CONSTRAINT ux_document_partial_part_group_part_active
            UNIQUE (tenant_id, partial_group_id, part_number);
    END IF;
END $$;

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
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS tenant_id uuid NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS user_id uuid NULL;
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
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
ALTER TABLE ged.app_audit_log ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

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

-- Empréstimos: itens manuais/físicos em solicitações.
-- Idempotente e seguro para tabelas com dados existentes.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.loan_request_item (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    loan_request_id uuid NULL,
    document_id uuid NULL,
    is_physical boolean NOT NULL DEFAULT false,
    is_manual boolean NOT NULL DEFAULT false,
    reference_code text NULL,
    description text NULL,
    document_type text NULL,
    patient_name text NULL,
    medical_record_number text NULL,
    box_code text NULL,
    physical_location text NULL,
    notes text NULL,
    document_version_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);

ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS is_manual boolean NOT NULL DEFAULT false;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS is_physical boolean NOT NULL DEFAULT false;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS reference_code text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS description text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS document_type text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS patient_name text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS medical_record_number text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS box_code text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS physical_location text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS notes text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema='ged'
          AND table_name='loan_request_item'
          AND column_name='request_id'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema='ged'
          AND table_name='loan_request_item'
          AND column_name='loan_request_id'
    )
    THEN
        ALTER TABLE ged.loan_request_item
        ADD COLUMN loan_request_id uuid;

        UPDATE ged.loan_request_item
        SET loan_request_id = request_id
        WHERE loan_request_id IS NULL;
    END IF;
END $$;

ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS loan_request_id uuid NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema='ged'
          AND table_name='loan_request_item'
          AND column_name='loan_id'
    )
    AND EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema='ged'
          AND table_name='loan_request_item'
          AND column_name='loan_request_id'
    )
    THEN
        UPDATE ged.loan_request_item
        SET loan_request_id = loan_id
        WHERE loan_request_id IS NULL;
    END IF;
END $$;

ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

UPDATE ged.loan_request_item
SET is_manual = COALESCE(is_manual, document_id IS NULL, false),
    created_at = COALESCE(created_at, now()),
    reg_status = COALESCE(reg_status, 'A')
WHERE is_manual IS NULL
   OR created_at IS NULL
   OR reg_status IS NULL;

CREATE INDEX IF NOT EXISTS ix_loan_request_item_loan_request
ON ged.loan_request_item(loan_request_id);

CREATE INDEX IF NOT EXISTS ix_loan_request_item_request
ON ged.loan_request_item(loan_request_id);

CREATE INDEX IF NOT EXISTS ix_loan_request_item_document
ON ged.loan_request_item(document_id);

CREATE INDEX IF NOT EXISTS ix_loan_request_item_manual
ON ged.loan_request_item(is_manual);

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

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='document_version_id') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ged_document_search_tenant_document_version ON ged.document_search(tenant_id, document_version_id)';
    ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='version_id') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ged_document_search_tenant_version ON ged.document_search(tenant_id, version_id)';
    ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='document_id') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ged_document_search_tenant_document ON ged.document_search(tenant_id, document_id)';
    ELSE
        RAISE NOTICE 'Índice ix_ged_document_search_tenant_version não criado: nenhuma coluna de versão/documento compatível encontrada em ged.document_search.';
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

-- Qualidade Documental: tabelas, colunas e índices idempotentes.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.document_quality_run (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    started_at_utc timestamptz not null default now(),
    finished_at_utc timestamptz null,
    status text not null default 'STARTED',
    total_documents int not null default 0,
    excellent_count int not null default 0,
    good_count int not null default 0,
    warning_count int not null default 0,
    critical_count int not null default 0,
    failed_count int not null default 0,
    message text null,
    correlation_id text null,
    created_at timestamptz not null default now()
);

create table if not exists ged.document_quality_result (
    id uuid primary key default gen_random_uuid(),
    run_id uuid null,
    tenant_id uuid not null,
    document_id uuid not null,
    current_version_id uuid null,
    quality_score int not null default 0,
    quality_status text not null default 'Não analisado',
    has_ocr boolean not null default false,
    has_ocr_error boolean not null default false,
    has_classification boolean not null default false,
    has_document_type boolean not null default false,
    has_required_metadata boolean not null default false,
    is_partial_document boolean not null default false,
    is_partial_incomplete boolean not null default false,
    is_ready_to_consolidate boolean not null default false,
    is_consolidated boolean not null default false,
    storage_file_exists boolean null,
    has_possible_duplicate boolean not null default false,
    has_lgpd_risk boolean not null default false,
    issues_json jsonb not null default '[]'::jsonb,
    recommendations_json jsonb not null default '[]'::jsonb,
    next_action text null,
    analyzed_at_utc timestamptz not null default now(),
    created_at timestamptz not null default now()
);

alter table ged.document_quality_run add column if not exists tenant_id uuid;
alter table ged.document_quality_run add column if not exists started_at_utc timestamptz not null default now();
alter table ged.document_quality_run add column if not exists finished_at_utc timestamptz null;
alter table ged.document_quality_run add column if not exists status text not null default 'STARTED';
alter table ged.document_quality_run add column if not exists total_documents int not null default 0;
alter table ged.document_quality_run add column if not exists excellent_count int not null default 0;
alter table ged.document_quality_run add column if not exists good_count int not null default 0;
alter table ged.document_quality_run add column if not exists warning_count int not null default 0;
alter table ged.document_quality_run add column if not exists critical_count int not null default 0;
alter table ged.document_quality_run add column if not exists failed_count int not null default 0;
alter table ged.document_quality_run add column if not exists message text null;
alter table ged.document_quality_run add column if not exists correlation_id text null;
alter table ged.document_quality_run add column if not exists created_at timestamptz not null default now();

alter table ged.document_quality_result add column if not exists run_id uuid null;
alter table ged.document_quality_result add column if not exists tenant_id uuid;
alter table ged.document_quality_result add column if not exists document_id uuid;
alter table ged.document_quality_result add column if not exists current_version_id uuid null;
alter table ged.document_quality_result add column if not exists quality_score int not null default 0;
alter table ged.document_quality_result add column if not exists quality_status text not null default 'Não analisado';
alter table ged.document_quality_result add column if not exists has_ocr boolean not null default false;
alter table ged.document_quality_result add column if not exists has_ocr_error boolean not null default false;
alter table ged.document_quality_result add column if not exists has_classification boolean not null default false;
alter table ged.document_quality_result add column if not exists has_document_type boolean not null default false;
alter table ged.document_quality_result add column if not exists has_required_metadata boolean not null default false;
alter table ged.document_quality_result add column if not exists is_partial_document boolean not null default false;
alter table ged.document_quality_result add column if not exists is_partial_incomplete boolean not null default false;
alter table ged.document_quality_result add column if not exists is_ready_to_consolidate boolean not null default false;
alter table ged.document_quality_result add column if not exists is_consolidated boolean not null default false;
alter table ged.document_quality_result add column if not exists storage_file_exists boolean null;
alter table ged.document_quality_result add column if not exists has_possible_duplicate boolean not null default false;
alter table ged.document_quality_result add column if not exists has_lgpd_risk boolean not null default false;
alter table ged.document_quality_result add column if not exists issues_json jsonb not null default '[]'::jsonb;
alter table ged.document_quality_result add column if not exists recommendations_json jsonb not null default '[]'::jsonb;
alter table ged.document_quality_result add column if not exists next_action text null;
alter table ged.document_quality_result add column if not exists analyzed_at_utc timestamptz not null default now();
alter table ged.document_quality_result add column if not exists created_at timestamptz not null default now();

create index if not exists ix_document_quality_result_tenant_document_analyzed
on ged.document_quality_result(tenant_id, document_id, analyzed_at_utc desc);

create index if not exists ix_document_quality_result_tenant_status
on ged.document_quality_result(tenant_id, quality_status);

create index if not exists ix_document_quality_result_tenant_score
on ged.document_quality_result(tenant_id, quality_score);

create index if not exists ix_document_quality_result_tenant_has_ocr
on ged.document_quality_result(tenant_id, has_ocr);

create index if not exists ix_document_quality_result_tenant_lgpd
on ged.document_quality_result(tenant_id, has_lgpd_risk);

create index if not exists ix_document_quality_result_run
on ged.document_quality_result(run_id);

create index if not exists ix_document_quality_run_tenant_started
on ged.document_quality_run(tenant_id, started_at_utc desc);

create index if not exists ix_document_quality_run_status
on ged.document_quality_run(tenant_id, status);
-- Fim Qualidade Documental.


-- Loans History / padronização resiliente de Loans.
-- Histórico idempotente de Loans.
-- Seguro para reexecução: não remove objetos e não apaga dados.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.loan_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid not null,
    old_status text null,
    new_status text null,
    action text not null default 'INFO',
    user_id uuid null,
    user_name text null,
    sector_id uuid null,
    sector_name text null,
    reason text null,
    internal_notes text null,
    metadata_json jsonb not null default '{}'::jsonb,
    correlation_id text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

alter table ged.loan_request_history
add column if not exists tenant_id uuid;

alter table ged.loan_request_history
add column if not exists loan_request_id uuid;

alter table ged.loan_request_history
add column if not exists old_status text null;

alter table ged.loan_request_history
add column if not exists new_status text null;

alter table ged.loan_request_history
add column if not exists action text not null default 'INFO';

alter table ged.loan_request_history
add column if not exists user_id uuid null;

alter table ged.loan_request_history
add column if not exists user_name text null;

alter table ged.loan_request_history
add column if not exists sector_id uuid null;

alter table ged.loan_request_history
add column if not exists sector_name text null;

alter table ged.loan_request_history
add column if not exists reason text null;

alter table ged.loan_request_history
add column if not exists internal_notes text null;

alter table ged.loan_request_history
add column if not exists metadata_json jsonb not null default '{}'::jsonb;

alter table ged.loan_request_history
add column if not exists correlation_id text null;

alter table ged.loan_request_history
add column if not exists created_at timestamptz not null default now();

alter table ged.loan_request_history
add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.loan_request add column if not exists requester_sector text null;
alter table if exists ged.loan_request add column if not exists sector_id uuid null;
alter table if exists ged.loan_request add column if not exists updated_at timestamptz null;
alter table if exists ged.loan_request add column if not exists updated_by uuid null;

alter table if exists ged.loan_request_item add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.loan_request_item add column if not exists description text null;
alter table if exists ged.loan_request_item add column if not exists reference_code text null;
alter table if exists ged.loan_request_item add column if not exists is_manual boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists document_version_id uuid null;
alter table if exists ged.loan_request_item add column if not exists loan_request_id uuid null;

create index if not exists ix_loan_request_history_tenant_loan_created
on ged.loan_request_history(tenant_id, loan_request_id, created_at desc);

create index if not exists ix_loan_request_history_tenant_action
on ged.loan_request_history(tenant_id, action);

create index if not exists ix_loan_request_history_tenant_created
on ged.loan_request_history(tenant_id, created_at desc);

create index if not exists ix_loan_request_history_tenant_user
on ged.loan_request_history(tenant_id, user_id);

-- Fim Loans History.


INSERT INTO ged.schema_migration_history(script_name, notes)
VALUES ('2026_06_ged_schema_consolidation.sql', 'Consolidação de schema GED/OCR/upload/logs/versionamento')
ON CONFLICT (script_name) DO UPDATE
SET applied_at = now(),
    success = true,
    notes = EXCLUDED.notes;

INSERT INTO ged.schema_migration_history(script_name, notes)
VALUES (
    'database/apply_all_required_migrations.sql',
    'Schema consolidado GED/OCR/upload/logs/documentos parciais/agendamento automático de OCR/empréstimos manuais/histórico de empréstimos/qualidade documental'
)
ON CONFLICT (script_name) DO UPDATE
SET applied_at = now(),
    success = true,
    notes = EXCLUDED.notes;

-- =======================================================
-- Módulo Protocolo (solicitações, itens, anexos, histórico)
-- Fonte: database/migrations/2026_06_protocol_module.sql
-- =======================================================
-- InovaGED - Módulo Protocolo ponta a ponta (idempotente)
CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS ged.code_sequence (
    tenant_id uuid NOT NULL,
    module text NOT NULL,
    year integer NOT NULL,
    current_value bigint NOT NULL DEFAULT 0,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_code_sequence PRIMARY KEY (tenant_id, module, year)
);

CREATE TABLE IF NOT EXISTS ged.protocol_request (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    protocol_no text NOT NULL,
    requester_user_id uuid NULL,
    requester_name text NULL,
    requester_sector_id uuid NULL,
    requester_sector_name text NULL,
    assigned_sector_id uuid NULL,
    assigned_sector_name text NULL,
    assigned_user_id uuid NULL,
    assigned_user_name text NULL,
    title text NOT NULL,
    description text NULL,
    priority text NOT NULL DEFAULT 'NORMAL',
    status text NOT NULL DEFAULT 'REQUESTED',
    due_at timestamptz NULL,
    requested_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    finished_at timestamptz NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    correlation_id text NULL
);

ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS requester_user_id uuid NULL;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS requester_sector_id uuid NULL;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS assigned_sector_id uuid NULL;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS assigned_user_id uuid NULL;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS correlation_id text NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_protocol_request_tenant_no ON ged.protocol_request(tenant_id, protocol_no) WHERE reg_status='A';
CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_status ON ged.protocol_request(tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_requester ON ged.protocol_request(tenant_id, requester_user_id);
CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_assigned_sector ON ged.protocol_request(tenant_id, assigned_sector_id);
CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_assigned_user ON ged.protocol_request(tenant_id, assigned_user_id);
CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_requested_desc ON ged.protocol_request(tenant_id, requested_at DESC);

CREATE TABLE IF NOT EXISTS ged.protocol_request_item (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    protocol_request_id uuid NOT NULL,
    document_id uuid NULL,
    document_version_id uuid NULL,
    is_manual boolean NOT NULL DEFAULT false,
    reference_code text NULL,
    description text NULL,
    document_type text NULL,
    patient_name text NULL,
    medical_record_number text NULL,
    box_code text NULL,
    physical_location text NULL,
    notes text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
CREATE INDEX IF NOT EXISTS ix_protocol_request_item_protocol ON ged.protocol_request_item(protocol_request_id);
CREATE INDEX IF NOT EXISTS ix_protocol_request_item_tenant_protocol ON ged.protocol_request_item(tenant_id, protocol_request_id);

CREATE TABLE IF NOT EXISTS ged.protocol_request_attachment (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    protocol_request_id uuid NOT NULL,
    file_name text NOT NULL,
    content_type text NULL,
    size_bytes bigint NULL,
    storage_path text NOT NULL,
    uploaded_by uuid NULL,
    uploaded_by_name text NULL,
    uploaded_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
CREATE INDEX IF NOT EXISTS ix_protocol_request_attachment_protocol ON ged.protocol_request_attachment(protocol_request_id);
CREATE INDEX IF NOT EXISTS ix_protocol_request_attachment_tenant_protocol ON ged.protocol_request_attachment(tenant_id, protocol_request_id);

CREATE TABLE IF NOT EXISTS ged.protocol_request_history (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    protocol_request_id uuid NOT NULL,
    old_status text NULL,
    new_status text NULL,
    action text NOT NULL,
    user_id uuid NULL,
    user_name text NULL,
    sector_id uuid NULL,
    sector_name text NULL,
    reason text NULL,
    internal_notes text NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    correlation_id text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
CREATE INDEX IF NOT EXISTS ix_protocol_request_history_protocol_created ON ged.protocol_request_history(protocol_request_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_protocol_request_history_tenant_protocol_created ON ged.protocol_request_history(tenant_id, protocol_request_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_protocol_request_history_tenant_action ON ged.protocol_request_history(tenant_id, action);

ALTER TABLE IF EXISTS ged.loan_request ADD COLUMN IF NOT EXISTS protocol_request_id uuid NULL;
CREATE INDEX IF NOT EXISTS ix_loan_request_protocol_request ON ged.loan_request(tenant_id, protocol_request_id) WHERE protocol_request_id IS NOT NULL;

INSERT INTO ged.schema_migration_history(script_name, notes)
SELECT '2026_06_protocol_module.sql', 'Módulo Protocolo: solicitações, itens GED/manuais, anexos, histórico e vínculo Loans'
WHERE to_regclass('ged.schema_migration_history') IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM ged.schema_migration_history WHERE script_name='2026_06_protocol_module.sql');
