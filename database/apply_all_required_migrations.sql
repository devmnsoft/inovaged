-- InovaGED - Consolidação idempotente de schema GED/OCR/upload/logs/versionamento.
-- Pode ser executado repetidas vezes. Não apaga dados e não sobrescreve registros reais.

-- Histórico de migrations / schema base
CREATE SCHEMA IF NOT EXISTS ged;
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pgcrypto. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pgcrypto: %', SQLERRM;
END $$;

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
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS size_bytes bigint NULL;
ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS content_hash text NULL;

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
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pgcrypto. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pgcrypto: %', SQLERRM;
END $$;
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
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pgcrypto. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pgcrypto: %', SQLERRM;
END $$;
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
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='ocr_job' AND column_name='status')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='ocr_job' AND column_name='requested_at') THEN
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='ocr_job' AND column_name='document_version_id') THEN
            EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job (tenant_id, document_version_id, status, requested_at DESC)';
        ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='ocr_job' AND column_name='version_id') THEN
            EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job (tenant_id, ' || quote_ident('version_id') || ', status, requested_at DESC)';
        ELSE
            RAISE NOTICE 'Índice ix_ocr_job_tenant_version_status não criado: ged.ocr_job não possui document_version_id nem version_id.';
        END IF;
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
-- InovaGED - Módulo Protocolo consolidado (idempotente)
-- Cria solicitações de protocolo, itens GED/manuais, anexos, histórico e vínculo com empréstimos.

DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pgcrypto. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pgcrypto: %', SQLERRM;
END $$;
CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.protocol_request (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_no text not null,
    requester_user_id uuid null,
    requester_name text null,
    requester_sector_id uuid null,
    requester_sector_name text null,
    assigned_sector_id uuid null,
    assigned_sector_name text null,
    assigned_user_id uuid null,
    assigned_user_name text null,
    title text not null default 'Solicitação de Protocolo',
    description text null,
    priority text not null default 'NORMAL',
    status text not null default 'REQUESTED',
    due_at timestamptz null,
    requested_at timestamptz not null default now(),
    updated_at timestamptz null,
    finished_at timestamptz null,
    reg_status char(1) not null default 'A',
    correlation_id text null,
    created_at timestamptz not null default now()
);

CREATE TABLE IF NOT EXISTS ged.protocol_request_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    document_id uuid null,
    document_version_id uuid null,
    is_manual boolean not null default false,
    reference_code text null,
    description text null,
    document_type text null,
    patient_name text null,
    medical_record_number text null,
    box_code text null,
    physical_location text null,
    notes text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

CREATE TABLE IF NOT EXISTS ged.protocol_request_attachment (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    file_name text not null,
    content_type text null,
    size_bytes bigint null,
    storage_path text not null,
    uploaded_by uuid null,
    uploaded_by_name text null,
    uploaded_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

CREATE TABLE IF NOT EXISTS ged.protocol_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
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

ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS protocol_no text;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS requester_user_id uuid null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS requester_name text null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS requester_sector_id uuid null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS requester_sector_name text null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS assigned_sector_id uuid null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS assigned_sector_name text null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS assigned_user_id uuid null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS assigned_user_name text null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS title text not null default 'Solicitação de Protocolo';
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS description text null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS priority text not null default 'NORMAL';
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS status text not null default 'REQUESTED';
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS due_at timestamptz null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS requested_at timestamptz not null default now();
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS updated_at timestamptz null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS finished_at timestamptz null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS reg_status char(1) not null default 'A';
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS correlation_id text null;
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS created_at timestamptz not null default now();

ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS protocol_request_id uuid;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS document_id uuid null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS document_version_id uuid null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS is_manual boolean not null default false;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS reference_code text null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS description text null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS document_type text null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS patient_name text null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS medical_record_number text null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS box_code text null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS physical_location text null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS notes text null;
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS created_at timestamptz not null default now();
ALTER TABLE ged.protocol_request_item ADD COLUMN IF NOT EXISTS reg_status char(1) not null default 'A';

ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS protocol_request_id uuid;
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS file_name text;
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS content_type text null;
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS size_bytes bigint null;
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS storage_path text;
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS uploaded_by uuid null;
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS uploaded_by_name text null;
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS uploaded_at timestamptz not null default now();
ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS reg_status char(1) not null default 'A';

ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS protocol_request_id uuid;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS old_status text null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS new_status text null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS action text not null default 'INFO';
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS user_id uuid null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS user_name text null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS sector_id uuid null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS sector_name text null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS reason text null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS internal_notes text null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS metadata_json jsonb not null default '{}'::jsonb;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS correlation_id text null;
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS created_at timestamptz not null default now();
ALTER TABLE ged.protocol_request_history ADD COLUMN IF NOT EXISTS reg_status char(1) not null default 'A';

ALTER TABLE IF EXISTS ged.loan_request ADD COLUMN IF NOT EXISTS protocol_request_id uuid null;

CREATE UNIQUE INDEX IF NOT EXISTS ux_protocol_request_tenant_protocol_no
ON ged.protocol_request(tenant_id, protocol_no);

CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_status
ON ged.protocol_request(tenant_id, status);

CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_requester
ON ged.protocol_request(tenant_id, requester_user_id);

CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_assigned_sector
ON ged.protocol_request(tenant_id, assigned_sector_id);

CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_assigned_user
ON ged.protocol_request(tenant_id, assigned_user_id);

CREATE INDEX IF NOT EXISTS ix_protocol_request_tenant_requested_at
ON ged.protocol_request(tenant_id, requested_at desc);

CREATE INDEX IF NOT EXISTS ix_protocol_request_item_protocol
ON ged.protocol_request_item(tenant_id, protocol_request_id);

CREATE INDEX IF NOT EXISTS ix_protocol_request_attachment_protocol
ON ged.protocol_request_attachment(tenant_id, protocol_request_id);

CREATE INDEX IF NOT EXISTS ix_protocol_request_history_protocol_created
ON ged.protocol_request_history(tenant_id, protocol_request_id, created_at desc);

DO $$
BEGIN
    IF to_regclass('ged.loan_request') IS NOT NULL THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_loan_request_protocol_request ON ged.loan_request(tenant_id, protocol_request_id)';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('ged.schema_migration_history') IS NOT NULL THEN
        INSERT INTO ged.schema_migration_history(script_name, notes)
        SELECT '2026_06_protocol_module.sql', 'Módulo Protocolo consolidado: solicitações, itens, anexos, histórico e vínculo Loans'
        WHERE NOT EXISTS (SELECT 1 FROM ged.schema_migration_history WHERE script_name='2026_06_protocol_module.sql');
    END IF;
END $$;
-- InovaGED - Busca Inteligente Conversacional (idempotente)
CREATE SCHEMA IF NOT EXISTS ged;
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pgcrypto. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pgcrypto: %', SQLERRM;
END $$;
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS unaccent;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão unaccent. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão unaccent: %', SQLERRM;
END $$;

DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pg_trgm. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pg_trgm: %', SQLERRM;
END $$;

CREATE TABLE IF NOT EXISTS ged.search_synonym (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    term text NOT NULL,
    synonym text NOT NULL,
    category text NULL,
    weight numeric NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);

ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS term text;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS synonym text;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS category text NULL;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS weight numeric NOT NULL DEFAULT 1;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.document_search_index (
    document_id uuid NOT NULL,
    version_id uuid NULL,
    tenant_id uuid NOT NULL,
    title text NULL,
    file_name text NULL,
    document_type text NULL,
    classification text NULL,
    folder_name text NULL,
    patient_name text NULL,
    medical_record_number text NULL,
    extracted_age int NULL,
    extracted_year int NULL,
    extracted_terms text[] NULL,
    ocr_text text NULL,
    search_text text NULL,
    search_vector tsvector NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, document_id)
);

ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS document_id uuid;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS title text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS file_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS document_type text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS classification text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS folder_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS patient_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS medical_record_number text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS extracted_age int NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS extracted_year int NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS extracted_terms text[] NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS ocr_text text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS search_text text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS search_vector tsvector NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS ged.search_query_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NULL,
    query_text text NULL,
    query_hash text NULL,
    interpreted_json jsonb NULL,
    results_count int NULL,
    clicked_document_id uuid NULL,
    duration_ms int NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS user_id uuid NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS query_text text NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS query_hash text NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS interpreted_json jsonb NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS results_count int NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS clicked_document_id uuid NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS duration_ms int NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS ged.document_access_stat (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    user_id uuid NULL,
    source text NULL,
    action text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS document_id uuid;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS user_id uuid NULL;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS source text NULL;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS action text NULL;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

CREATE INDEX IF NOT EXISTS ix_search_synonym_tenant_term ON ged.search_synonym(tenant_id, lower(term));
CREATE INDEX IF NOT EXISTS ix_search_synonym_tenant_synonym ON ged.search_synonym(tenant_id, lower(synonym));
CREATE INDEX IF NOT EXISTS ix_document_search_index_vector ON ged.document_search_index USING GIN(search_vector);
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_search_index_text_trgm ON ged.document_search_index USING GIN(search_text gin_trgm_ops)';
    ELSE
        RAISE NOTICE 'Índice trigram não criado: extensão pg_trgm ausente.';
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant ON ged.document_search_index(tenant_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_document ON ged.document_search_index(document_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_age ON ged.document_search_index(tenant_id, extracted_age);
CREATE INDEX IF NOT EXISTS ix_document_search_index_year ON ged.document_search_index(tenant_id, extracted_year);
CREATE INDEX IF NOT EXISTS ix_search_query_log_tenant_created ON ged.search_query_log(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_search_query_log_tenant_user ON ged.search_query_log(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_created ON ged.document_access_stat(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_document ON ged.document_access_stat(tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_user ON ged.document_access_stat(tenant_id, user_id);

DO $$
DECLARE seed_tenant uuid;
BEGIN
    FOR seed_tenant IN SELECT DISTINCT tenant_id FROM ged.document WHERE tenant_id IS NOT NULL LOOP
        INSERT INTO ged.search_synonym(tenant_id, term, synonym, category, weight)
        VALUES
        (seed_tenant, 'AVC', 'acidente vascular cerebral', 'clinical', 1),
        (seed_tenant, 'diabetes', 'diabete', 'clinical', 1),
        (seed_tenant, 'diabetes', 'DM', 'clinical', 1),
        (seed_tenant, 'tomografia', 'TC', 'exam', 1),
        (seed_tenant, 'tomografia', 'tomografia computadorizada', 'exam', 1),
        (seed_tenant, 'raio x', 'radiografia', 'exam', 1),
        (seed_tenant, 'raio x', 'RX', 'exam', 1),
        (seed_tenant, 'câncer', 'neoplasia', 'clinical', 1),
        (seed_tenant, 'câncer', 'tumor', 'clinical', 1),
        (seed_tenant, 'rim', 'renal', 'clinical', 1),
        (seed_tenant, 'coração', 'cardíaco', 'clinical', 1),
        (seed_tenant, 'coração', 'cardiológico', 'clinical', 1),
        (seed_tenant, 'exame', 'laudo', 'document', 1),
        (seed_tenant, 'exame', 'resultado', 'document', 1)
        ON CONFLICT DO NOTHING;
    END LOOP;
END $$;

-- Applying 2026_06_ged_processing_pipeline.sql
CREATE SCHEMA IF NOT EXISTS ged;
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pgcrypto. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pgcrypto: %', SQLERRM;
END $$;

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

ALTER TABLE ged.processing_job ADD COLUMN IF NOT EXISTS payload jsonb NOT NULL DEFAULT '{}'::jsonb;

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

-- Applying 2026_06_ged_search_intelligence.sql
-- InovaGED - GED Smart Search Intelligence (idempotent, text-only)
CREATE SCHEMA IF NOT EXISTS ged;
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pgcrypto. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pgcrypto: %', SQLERRM;
END $$;
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS unaccent;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão unaccent. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão unaccent: %', SQLERRM;
END $$;
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar extensão pg_trgm. Continuando sem ela.';
WHEN others THEN
    RAISE NOTICE 'Não foi possível criar extensão pg_trgm: %', SQLERRM;
END $$;

CREATE TABLE IF NOT EXISTS ged.search_synonym (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    term text NOT NULL,
    synonym text NOT NULL,
    category text NULL,
    weight numeric NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_search_synonym_tenant_term_synonym ON ged.search_synonym(tenant_id, lower(term), lower(synonym));

CREATE TABLE IF NOT EXISTS ged.document_search_index (
    id uuid DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NULL,
    version_id uuid NULL,
    folder_id uuid NULL,
    title text NULL,
    file_name text NULL,
    document_type text NULL,
    classification_name text NULL,
    classification text NULL,
    folder_name text NULL,
    patient_name text NULL,
    medical_record_number text NULL,
    protocol_number text NULL,
    extracted_age int NULL,
    extracted_year int NULL,
    extracted_terms text[] NULL,
    ocr_text text NULL,
    search_text text NOT NULL DEFAULT '',
    search_vector tsvector NULL,
    last_indexed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    PRIMARY KEY (tenant_id, document_id)
);
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid();
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS classification_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS classification text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS protocol_number text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS last_indexed_at timestamptz NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';
ALTER TABLE ged.document_search_index ALTER COLUMN search_text SET DEFAULT '';
UPDATE ged.document_search_index SET search_text = '' WHERE search_text IS NULL;

CREATE TABLE IF NOT EXISTS ged.search_query_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NULL,
    query_text text NULL,
    query_hash text NULL,
    interpreted_json jsonb NULL,
    results_count int NOT NULL DEFAULT 0,
    duration_ms int NOT NULL DEFAULT 0,
    clicked_document_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS duration_ms int NOT NULL DEFAULT 0;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS clicked_document_id uuid NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.document_access_stat (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NULL,
    document_id uuid NOT NULL,
    source text NOT NULL,
    action text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE ged.document_access_stat ALTER COLUMN source SET DEFAULT 'SMART_SEARCH';
ALTER TABLE ged.document_access_stat ALTER COLUMN action SET DEFAULT 'ACCESS';

CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_document ON ged.document_search_index(tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_folder ON ged.document_search_index(tenant_id, folder_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_year ON ged.document_search_index(tenant_id, extracted_year);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_age ON ged.document_search_index(tenant_id, extracted_age);
CREATE INDEX IF NOT EXISTS ix_document_search_index_vector ON ged.document_search_index USING GIN(search_vector);
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_search_index_text_trgm ON ged.document_search_index USING GIN(search_text gin_trgm_ops)';
    ELSE
        RAISE NOTICE 'Índice trigram não criado: extensão pg_trgm ausente.';
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_search_query_log_tenant_created ON ged.search_query_log(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_search_query_log_tenant_user ON ged.search_query_log(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS ix_search_query_log_query_hash ON ged.search_query_log(query_hash);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_created ON ged.document_access_stat(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_user ON ged.document_access_stat(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_document ON ged.document_access_stat(tenant_id, document_id);

DO $$
DECLARE seed_tenant uuid;
BEGIN
  FOR seed_tenant IN SELECT DISTINCT tenant_id FROM ged.document WHERE tenant_id IS NOT NULL LOOP
    INSERT INTO ged.search_synonym(tenant_id, term, synonym, category, weight) VALUES
    (seed_tenant,'AVC','acidente vascular cerebral','clinical',1),(seed_tenant,'AVC','derrame','clinical',1),
    (seed_tenant,'diabetes','diabete','clinical',1),(seed_tenant,'diabetes','dm','clinical',1),
    (seed_tenant,'tomografia','tc','exam',1),(seed_tenant,'tomografia','tomografia computadorizada','exam',1),
    (seed_tenant,'raio-x','rx','exam',1),(seed_tenant,'raio-x','radiografia','exam',1),
    (seed_tenant,'câncer','neoplasia','clinical',1),(seed_tenant,'câncer','tumor','clinical',1),
    (seed_tenant,'renal','rim','clinical',1),(seed_tenant,'renal','rins','clinical',1),(seed_tenant,'renal','nefrologia','clinical',1),
    (seed_tenant,'cardíaco','coração','clinical',1),(seed_tenant,'cardíaco','cardiologia','clinical',1),
    (seed_tenant,'ultrassom','ultrassonografia','exam',1),(seed_tenant,'ultrassom','usg','exam',1),
    (seed_tenant,'laboratório','exame laboratorial','exam',1),(seed_tenant,'laboratório','resultado laboratorial','exam',1)
    ON CONFLICT DO NOTHING;
  END LOOP;
END $$;

DO $$
BEGIN
  IF to_regclass('ged.processing_job') IS NOT NULL THEN
    INSERT INTO ged.processing_job(tenant_id, job_type, status, payload, created_at)
    SELECT DISTINCT tenant_id, 'SMART_INDEX', 'PENDING', '{}'::jsonb, now()
    FROM ged.document d
    WHERE d.tenant_id IS NOT NULL
    ON CONFLICT DO NOTHING;
  END IF;
END $$;

-- Applying 2026_06_upload_batch_incomplete_options.sql
alter table ged.upload_batch
add column if not exists options_json jsonb not null default '{}'::jsonb;

alter table ged.upload_batch_item
add column if not exists mark_as_incomplete boolean not null default false;

alter table ged.upload_batch_item
add column if not exists incomplete_reason text null;

alter table ged.document
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document
add column if not exists incomplete_reason text null;

alter table ged.document_version
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document_version
add column if not exists incomplete_reason text null;

alter table ged.document
add column if not exists incomplete_source text null;

alter table ged.document_version
add column if not exists incomplete_source text null;

-- Applying 2026_06_upload_batch_resilience.sql
create schema if not exists ged;

alter table ged.upload_batch
add column if not exists options_json jsonb not null default '{}'::jsonb;

alter table ged.upload_batch
add column if not exists updated_at timestamptz null;

alter table ged.upload_batch_item
add column if not exists upload_client_id text null;

alter table ged.upload_batch_item
add column if not exists content_hash text null;

alter table ged.upload_batch_item
add column if not exists mark_as_incomplete boolean not null default false;

alter table ged.upload_batch_item
add column if not exists incomplete_reason text null;

alter table ged.upload_batch_item
add column if not exists retry_after_at timestamptz null;

alter table ged.upload_batch_item
add column if not exists updated_at timestamptz null;

alter table ged.document
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document
add column if not exists incomplete_reason text null;

alter table ged.document
add column if not exists incomplete_source text null;

alter table ged.document_version
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document_version
add column if not exists incomplete_reason text null;

alter table ged.document_version
add column if not exists incomplete_source text null;

create index if not exists ix_upload_batch_item_tenant_batch_status
on ged.upload_batch_item(tenant_id, batch_id, status);

create index if not exists ix_upload_batch_item_tenant_hash
on ged.upload_batch_item(tenant_id, batch_id, content_hash);

create unique index if not exists ux_upload_batch_item_dedup
on ged.upload_batch_item(tenant_id, batch_id, original_file_name, size_bytes, content_hash)
where coalesce(reg_status,'A')='A'
  and content_hash is not null;

create index if not exists ix_document_incomplete
on ged.document(tenant_id, is_document_incomplete)
where coalesce(reg_status,'A')='A';

-- Applying 2026_06_upload_batch_incomplete_and_resilience.sql
create schema if not exists ged;

alter table ged.upload_batch
add column if not exists options_json jsonb not null default '{}'::jsonb;

alter table ged.upload_batch
add column if not exists updated_at timestamptz null;

alter table ged.upload_batch_item
add column if not exists upload_client_id text null;

alter table ged.upload_batch_item
add column if not exists content_hash text null;

alter table ged.upload_batch_item
add column if not exists mark_as_incomplete boolean not null default false;

alter table ged.upload_batch_item
add column if not exists incomplete_reason text null;

alter table ged.upload_batch_item
add column if not exists retry_after_at timestamptz null;

alter table ged.upload_batch_item
add column if not exists updated_at timestamptz null;

alter table ged.document
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document
add column if not exists incomplete_reason text null;

alter table ged.document
add column if not exists incomplete_source text null;

alter table ged.document_version
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document_version
add column if not exists incomplete_reason text null;

alter table ged.document_version
add column if not exists incomplete_source text null;

create index if not exists ix_upload_batch_item_tenant_batch_status
on ged.upload_batch_item(tenant_id, batch_id, status);

create index if not exists ix_upload_batch_item_tenant_hash
on ged.upload_batch_item(tenant_id, batch_id, content_hash);

create unique index if not exists ux_upload_batch_item_dedup
on ged.upload_batch_item(tenant_id, batch_id, original_file_name, size_bytes, content_hash)
where coalesce(reg_status,'A')='A'
  and content_hash is not null;

create index if not exists ix_document_incomplete
on ged.document(tenant_id, is_document_incomplete)
where coalesce(reg_status,'A')='A';

-- Views/materialized views condicionais.
DO $$
BEGIN
    IF to_regclass('ged.ocr_job') IS NOT NULL
       AND to_regclass('ged.mv_dashboard_ocr') IS NULL THEN
        EXECUTE '
            CREATE MATERIALIZED VIEW ged.mv_dashboard_ocr AS
            SELECT tenant_id, status, count(*) AS total
            FROM ged.ocr_job
            GROUP BY tenant_id, status
        ';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('ged.mv_dashboard_ocr') IS NOT NULL THEN
        EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS ix_mv_dashboard_ocr ON ged.mv_dashboard_ocr (tenant_id, status)';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('ged.schema_migration_history') IS NOT NULL THEN
        INSERT INTO ged.schema_migration_history(script_name, notes)
        VALUES ('database/apply_all_required_migrations.sql', 'Script master SQL puro aplicado com migrations obrigatórias incorporadas')
        ON CONFLICT (script_name) DO UPDATE
        SET applied_at = now(), success = true, notes = EXCLUDED.notes;
    END IF;
END $$;

DO $$
BEGIN
    RAISE NOTICE 'Schema GED aplicado com sucesso.';

    IF to_regclass('ged.processing_job') IS NULL THEN
        RAISE NOTICE 'Atenção: ged.processing_job ainda ausente.';
    END IF;

    IF to_regclass('ged.ocr_job') IS NULL THEN
        RAISE NOTICE 'Atenção: ged.ocr_job ainda ausente.';
    END IF;

    IF to_regclass('ged.protocol_request') IS NULL THEN
        RAISE NOTICE 'Atenção: ged.protocol_request ainda ausente.';
    END IF;
END $$;

-- Included migration: database/migrations/2026_06_fix_smart_search_index.sql
-- Applying 2026_06_fix_smart_search_index.sql
-- Compatibilidade e robustez para SmartSearch/GED. Idempotente e textual.
CREATE SCHEMA IF NOT EXISTS ged;
DO $$ BEGIN CREATE EXTENSION IF NOT EXISTS unaccent; EXCEPTION WHEN insufficient_privilege THEN RAISE NOTICE 'Sem permissão para unaccent.'; WHEN others THEN RAISE NOTICE 'unaccent indisponível: %', SQLERRM; END $$;
DO $$ BEGIN CREATE EXTENSION IF NOT EXISTS pg_trgm; EXCEPTION WHEN insufficient_privilege THEN RAISE NOTICE 'Sem permissão para pg_trgm.'; WHEN others THEN RAISE NOTICE 'pg_trgm indisponível: %', SQLERRM; END $$;

CREATE TABLE IF NOT EXISTS ged.document_search_index (
    id uuid DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NULL,
    version_id uuid NULL,
    title text NULL,
    file_name text NULL,
    document_type text NULL,
    classification text NULL,
    classification_name text NULL,
    folder_id uuid NULL,
    folder_name text NULL,
    patient_name text NULL,
    medical_record_number text NULL,
    protocol_number text NULL,
    extracted_age int NULL,
    extracted_year int NULL,
    extracted_terms text[] NULL,
    ocr_text text NULL,
    search_text text NOT NULL DEFAULT '',
    search_vector tsvector NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    last_indexed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_document_search_index_tenant_document UNIQUE (tenant_id, document_id)
);

ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS search_text text NOT NULL DEFAULT '';
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS last_indexed_at timestamptz NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS classification_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS protocol_number text NULL;

UPDATE ged.document_search_index SET search_text = coalesce(search_text, '');
UPDATE ged.document_search_index SET document_version_id = version_id WHERE document_version_id IS NULL AND version_id IS NOT NULL;
UPDATE ged.document_search_index SET version_id = document_version_id WHERE version_id IS NULL AND document_version_id IS NOT NULL;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname='unaccent') THEN
        UPDATE ged.document_search_index SET search_vector = to_tsvector('portuguese', unaccent(coalesce(search_text,''))) WHERE search_vector IS NULL;
    ELSE
        UPDATE ged.document_search_index SET search_vector = to_tsvector('portuguese', coalesce(search_text,'')) WHERE search_vector IS NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_document ON ged.document_search_index(tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_folder ON ged.document_search_index(tenant_id, folder_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_vector ON ged.document_search_index USING GIN(search_vector);
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname='pg_trgm') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_search_index_text_trgm ON ged.document_search_index USING GIN(search_text gin_trgm_ops)';
    END IF;
END $$;


-- OCR environment diagnostics and structured external-process failures
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='ged' AND table_name='ocr_job') THEN
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS error_details_json jsonb NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS started_at timestamptz NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS attempt_count int NOT NULL DEFAULT 0;
        ALTER TABLE ged.ocr_job ADD COLUMN IF NOT EXISTS worker_id text NULL;
    END IF;
END $$;
create schema if not exists ged;

alter table if exists ged.upload_batch_item
add column if not exists status text not null default 'PENDING';

alter table if exists ged.upload_batch_item
add column if not exists processing_warning text null;

-- Normalizar valores nulos/vazios antes da constraint
update ged.upload_batch_item
set status = 'PENDING'
where status is null or trim(status) = '';

-- Corrigir status legados incompatíveis, se existirem
update ged.upload_batch_item
set status = 'CANCELLED'
where upper(status) = 'CANCELED';

update ged.upload_batch_item
set status = 'QUEUED'
where upper(status) in ('OCR_QUEUED', 'PREVIEW_QUEUED', 'SMART_INDEX_QUEUED');

update ged.upload_batch_item
set status = 'ERROR'
where upper(status) in ('FAILED', 'FAILURE');

update ged.upload_batch_item
set status = 'COMPLETED'
where upper(status) in ('DONE', 'SUCCESS');

update ged.upload_batch_item
set status = upper(status)
where status <> upper(status);

update ged.upload_batch_item
set status = 'PENDING', processing_warning = concat_ws(' | ', processing_warning, 'Status legado incompatível normalizado para PENDING: ' || status)
where status not in (
    'PENDING',
    'RECEIVING',
    'SAVED',
    'DOCUMENT_CREATED',
    'QUEUED',
    'COMPLETED',
    'ERROR',
    'SKIPPED',
    'ABORTED',
    'RETRYABLE',
    'DUPLICATE',
    'CANCELLED'
);

-- Remover constraint antiga, se existir
do $$
begin
    if exists (
        select 1
        from pg_constraint
        where conname = 'ck_upload_batch_item_status'
          and conrelid = 'ged.upload_batch_item'::regclass
    ) then
        alter table ged.upload_batch_item
        drop constraint ck_upload_batch_item_status;
    end if;
end $$;

-- Recriar constraint com todos os status usados pelo código
alter table ged.upload_batch_item
add constraint ck_upload_batch_item_status
check (
    status in (
        'PENDING',
        'RECEIVING',
        'SAVED',
        'DOCUMENT_CREATED',
        'QUEUED',
        'COMPLETED',
        'ERROR',
        'SKIPPED',
        'ABORTED',
        'RETRYABLE',
        'DUPLICATE',
        'CANCELLED'
    )
);

create index if not exists ix_upload_batch_item_tenant_batch_status
on ged.upload_batch_item(tenant_id, batch_id, status);

create index if not exists ix_upload_batch_item_retryable
on ged.upload_batch_item(tenant_id, batch_id, status, can_retry)
where status in ('ERROR', 'ABORTED', 'RETRYABLE');

-- Applying 2026_06_ged_bulk_actions_and_upload_logs.sql
create schema if not exists ged;

alter table ged.document add column if not exists is_document_incomplete boolean not null default false;
alter table ged.document add column if not exists incomplete_reason text null;
alter table ged.document add column if not exists incomplete_source text null;
alter table ged.document add column if not exists deleted_at timestamptz null;
alter table ged.document add column if not exists deleted_by uuid null;
alter table ged.document add column if not exists deleted_reason text null;
alter table ged.document add column if not exists updated_at timestamptz null;
alter table ged.document add column if not exists updated_by uuid null;

alter table ged.document_version add column if not exists is_document_incomplete boolean not null default false;
alter table ged.document_version add column if not exists incomplete_reason text null;
alter table ged.document_version add column if not exists incomplete_source text null;

alter table ged.upload_batch add column if not exists finished_at timestamptz null;
alter table ged.upload_batch add column if not exists updated_at timestamptz null;
alter table ged.upload_batch add column if not exists source_ip text null;
alter table ged.upload_batch add column if not exists user_agent text null;
alter table ged.upload_batch add column if not exists correlation_id text null;
alter table ged.upload_batch add column if not exists options_json jsonb not null default '{}'::jsonb;

alter table ged.upload_batch_item add column if not exists error_step text null;
alter table ged.upload_batch_item add column if not exists can_retry boolean not null default false;
alter table ged.upload_batch_item add column if not exists finished_at timestamptz null;
alter table ged.upload_batch_item add column if not exists elapsed_ms bigint null;
alter table ged.upload_batch_item add column if not exists processing_warning text null;
alter table ged.upload_batch_item add column if not exists updated_at timestamptz null;

create index if not exists ix_document_tenant_incomplete on ged.document(tenant_id, is_document_incomplete) where coalesce(reg_status,'A')='A';
create index if not exists ix_document_tenant_deleted on ged.document(tenant_id, deleted_at) where deleted_at is not null;
create index if not exists ix_upload_batch_tenant_created on ged.upload_batch(tenant_id, created_at desc);
create index if not exists ix_upload_batch_item_tenant_batch_status on ged.upload_batch_item(tenant_id, batch_id, status);
create index if not exists ix_upload_batch_item_retry on ged.upload_batch_item(tenant_id, batch_id, can_retry, status) where can_retry = true;


-- Applying 2026_06_upload_batch_acknowledgement.sql
-- Persistência de reconhecimento operacional de lotes de upload GED.
alter table ged.upload_batch
add column if not exists acknowledged_at timestamptz null;

alter table ged.upload_batch
add column if not exists acknowledged_by uuid null;

alter table ged.upload_batch
add column if not exists user_notes text null;

alter table ged.upload_batch
add column if not exists problem_seen boolean not null default false;

create index if not exists ix_upload_batch_last_problem_user
on ged.upload_batch(tenant_id, created_by, created_at desc)
where coalesce(reg_status,'A')='A';


-- Applying 2026_06_fix_upload_batch_user_display_name.sql
create schema if not exists ged;

alter table ged.upload_batch
add column if not exists created_by_name text null;

alter table ged.upload_batch_item
add column if not exists uploaded_by_name text null;

update ged.upload_batch
set created_by_name = coalesce(created_by_name, created_by::text)
where created_by_name is null
  and created_by is not null;

create index if not exists ix_upload_batch_tenant_created_by
on ged.upload_batch(tenant_id, created_by, created_at desc);

-- Applying 2026_06_harden_ged_uploads_schema.sql
create schema if not exists ged;

alter table ged.upload_batch add column if not exists created_by uuid null;
alter table ged.upload_batch add column if not exists created_by_name text null;
alter table ged.upload_batch add column if not exists folder_id uuid null;
alter table ged.upload_batch add column if not exists requested_folder_id uuid null;
alter table ged.upload_batch add column if not exists status text not null default 'OPEN';
alter table ged.upload_batch add column if not exists total_files int not null default 0;
alter table ged.upload_batch add column if not exists success_files int not null default 0;
alter table ged.upload_batch add column if not exists failed_files int not null default 0;
alter table ged.upload_batch add column if not exists skipped_files int not null default 0;
alter table ged.upload_batch add column if not exists source_ip text null;
alter table ged.upload_batch add column if not exists user_agent text null;
alter table ged.upload_batch add column if not exists correlation_id text null;
alter table ged.upload_batch add column if not exists started_at timestamptz null;
alter table ged.upload_batch add column if not exists finished_at timestamptz null;
alter table ged.upload_batch add column if not exists created_at timestamptz not null default now();
alter table ged.upload_batch add column if not exists updated_at timestamptz null;
alter table ged.upload_batch add column if not exists reg_status char(1) not null default 'A';
alter table ged.upload_batch add column if not exists acknowledged_at timestamptz null;
alter table ged.upload_batch add column if not exists acknowledged_by uuid null;
alter table ged.upload_batch add column if not exists problem_seen boolean not null default false;
alter table ged.upload_batch add column if not exists user_notes text null;
alter table ged.upload_batch add column if not exists options_json jsonb not null default '{}'::jsonb;

update ged.upload_batch
set created_by_name = coalesce(nullif(created_by_name, ''), created_by::text, 'Usuário não identificado')
where created_by_name is null
   or trim(created_by_name) = '';

create index if not exists ix_upload_batch_tenant_created on ged.upload_batch(tenant_id, created_at desc);
create index if not exists ix_upload_batch_tenant_created_by on ged.upload_batch(tenant_id, created_by, created_at desc);
create index if not exists ix_upload_batch_tenant_status on ged.upload_batch(tenant_id, status, created_at desc);

alter table ged.upload_batch_item add column if not exists uploaded_by_name text null;
alter table ged.upload_batch_item add column if not exists processing_warning text null;
alter table ged.upload_batch_item add column if not exists error_step text null;
alter table ged.upload_batch_item add column if not exists can_retry boolean not null default false;
alter table ged.upload_batch_item add column if not exists elapsed_ms bigint null;
alter table ged.upload_batch_item add column if not exists finished_at timestamptz null;
alter table ged.upload_batch_item add column if not exists updated_at timestamptz null;
create schema if not exists ged;

alter table ged.ocr_job
add column if not exists error_message text null;

alter table ged.ocr_job
add column if not exists error_details_json jsonb null;

alter table ged.ocr_job
add column if not exists started_at timestamptz null;

alter table ged.ocr_job
add column if not exists finished_at timestamptz null;

alter table ged.ocr_job
add column if not exists attempt_count int not null default 0;

alter table ged.ocr_job
add column if not exists worker_id text null;

alter table ged.ocr_job
add column if not exists locked_at timestamptz null;

alter table ged.ocr_job
add column if not exists locked_by text null;

alter table ged.ocr_job
add column if not exists updated_at timestamptz null;

alter table ged.ocr_job
add column if not exists next_attempt_at timestamptz null;

alter table ged.ocr_job
add column if not exists failure_code text null;

alter table ged.ocr_job
add column if not exists reg_status char(1) not null default 'A';

create index if not exists ix_ocr_job_tenant_status_requested
on ged.ocr_job(tenant_id, status, requested_at desc);

create index if not exists ix_ocr_job_tenant_version
on ged.ocr_job(tenant_id, document_version_id);

create index if not exists ix_ocr_job_tenant_failure_code
on ged.ocr_job(tenant_id, failure_code)
where failure_code is not null;

create index if not exists ix_ocr_job_tenant_next_attempt
on ged.ocr_job(tenant_id, next_attempt_at)
where next_attempt_at is not null;

create index if not exists ix_ocr_job_tenant_worker_lock
on ged.ocr_job(tenant_id, locked_by, locked_at)
where locked_by is not null;

do $$
begin
    if to_regclass('ged.schema_migration_history') is not null then
        insert into ged.schema_migration_history(script_name, notes)
        values ('2026_06_ocr_job_diagnostics.sql', 'Campos de diagnóstico técnico e retry do OCR Job')
        on conflict (script_name) do update
        set applied_at = now(),
            success = true,
            notes = excluded.notes;
    end if;
end $$;

-- Applying 2026_06_fix_parameters_create.sql (inlined; SQL puro)
create schema if not exists ged;

create extension if not exists pgcrypto;

create table if not exists ged.parameter_category (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    code text not null,
    name text not null,
    description text null,
    icon text null,
    display_order int not null default 0,
    is_system boolean not null default false,
    allow_hierarchy boolean not null default false,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);

create table if not exists ged.parameter_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    category_id uuid not null,
    parent_id uuid null,
    code text not null,
    name text not null,
    description text null,
    abbreviation text null,
    external_code text null,
    color text null,
    icon text null,
    metadata_json jsonb null,
    display_order int not null default 0,
    is_default boolean not null default false,
    is_system boolean not null default false,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid null,
    updated_at timestamptz null,
    updated_by uuid null,
    reg_status char(1) not null default 'A'
);

alter table ged.parameter_item add column if not exists created_by uuid null;
alter table ged.parameter_item add column if not exists updated_by uuid null;
alter table ged.parameter_item add column if not exists metadata_json jsonb null;
alter table ged.parameter_item add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.parameter_item_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    item_id uuid not null,
    category_code text null,
    action text not null,
    changed_by uuid null,
    old_data jsonb null,
    new_data jsonb null,
    reason text null,
    created_at timestamptz not null default now()
);

alter table ged.parameter_item_history add column if not exists tenant_id uuid;
alter table ged.parameter_item_history add column if not exists item_id uuid;
alter table ged.parameter_item_history add column if not exists category_code text null;
alter table ged.parameter_item_history add column if not exists action text;
alter table ged.parameter_item_history add column if not exists changed_by uuid null;
alter table ged.parameter_item_history add column if not exists old_data jsonb null;
alter table ged.parameter_item_history add column if not exists new_data jsonb null;
alter table ged.parameter_item_history add column if not exists reason text null;
alter table ged.parameter_item_history add column if not exists created_at timestamptz not null default now();

create unique index if not exists ux_parameter_category_tenant_code
on ged.parameter_category(tenant_id, code)
where coalesce(reg_status,'A')='A';

create unique index if not exists ux_parameter_item_tenant_category_code
on ged.parameter_item(tenant_id, category_id, code)
where coalesce(reg_status,'A')='A';

create index if not exists ix_parameter_item_tenant_category
on ged.parameter_item(tenant_id, category_id, display_order, name);

-- InovaGED - Parameters, SmartSearch, GED UI/navigation and BI support (idempotent, text-only)

create index if not exists ix_document_tenant_created
on ged.document(tenant_id, created_at desc);

create index if not exists ix_document_tenant_folder_created
on ged.document(tenant_id, folder_id, created_at desc);

create index if not exists ix_document_version_tenant_document
on ged.document_version(tenant_id, document_id);

do $$
begin
    if to_regclass('ged.upload_batch') is not null then
        create index if not exists ix_upload_batch_tenant_created_status
        on ged.upload_batch(tenant_id, created_at desc, status);
    end if;

    if to_regclass('ged.upload_batch_item') is not null then
        create index if not exists ix_upload_batch_item_tenant_status
        on ged.upload_batch_item(tenant_id, status);
    end if;

    if to_regclass('ged.search_query_log') is not null then
        create index if not exists ix_search_query_log_tenant_created
        on ged.search_query_log(tenant_id, created_at desc);
    end if;

    if to_regclass('ged.document_search_index') is not null then
        create index if not exists ix_document_search_index_tenant_document
        on ged.document_search_index(tenant_id, document_id);
        create index if not exists ix_document_search_index_tenant_folder
        on ged.document_search_index(tenant_id, folder_id);
    end if;
end $$;

create table if not exists ged.folder_virtual_map
(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    virtual_folder_id uuid not null,
    real_folder_id uuid not null,
    created_at timestamptz not null default now(),
    created_by uuid null,
    reg_status char(1) not null default 'A'
);

create unique index if not exists ux_folder_virtual_map_active
on ged.folder_virtual_map(tenant_id, virtual_folder_id)
where reg_status='A';

create index if not exists ix_folder_virtual_map_real
on ged.folder_virtual_map(tenant_id, real_folder_id)
where reg_status='A';
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.code_sequence (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    entity_name text not null,
    prefix text not null,
    current_value bigint not null default 0,
    padding int not null default 4,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);

alter table ged.code_sequence add column if not exists tenant_id uuid;
alter table ged.code_sequence add column if not exists entity_name text;
alter table ged.code_sequence add column if not exists prefix text;
alter table ged.code_sequence alter column prefix set default 'COD';
update ged.code_sequence set prefix='COD' where prefix is null;
alter table ged.code_sequence alter column prefix set not null;
alter table ged.code_sequence add column if not exists current_value bigint not null default 0;
alter table ged.code_sequence add column if not exists padding int not null default 4;
alter table ged.code_sequence add column if not exists created_at timestamptz not null default now();
alter table ged.code_sequence add column if not exists updated_at timestamptz null;
alter table ged.code_sequence add column if not exists reg_status char(1) not null default 'A';

create unique index if not exists ux_code_sequence_tenant_entity
on ged.code_sequence(tenant_id, entity_name)
where coalesce(reg_status,'A')='A';

create index if not exists ix_code_sequence_tenant
on ged.code_sequence(tenant_id);

insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding)
select distinct tenant_id, 'ParameterItem:LOTACAO', 'LOT', 0, 4
from ged.parameter_category
where code='LOTACAO'
on conflict do nothing;

insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding)
select distinct tenant_id, 'ParameterItem:TIPO_DOCUMENTO', 'TIP', 0, 4
from ged.parameter_category
where code='TIPO_DOCUMENTO'
on conflict do nothing;

insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding)
select distinct tenant_id, 'ParameterItem:CLASSIFICACAO', 'CLA', 0, 4
from ged.parameter_category
where code='CLASSIFICACAO'
on conflict do nothing;

do $$
begin
    if to_regclass('ged.schema_migration_history') is not null then
        insert into ged.schema_migration_history(script_name, notes)
        values ('2026_06_code_sequence.sql', 'Tabela de sequência de códigos automáticos')
        on conflict (script_name) do update
        set applied_at = now(),
            success = true,
            notes = excluded.notes;
    end if;
end $$;
create schema if not exists ged;

create extension if not exists pgcrypto;

create table if not exists ged.code_sequence (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    entity_name text not null,
    prefix text not null,
    current_value bigint not null default 0,
    padding int not null default 4,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);

alter table ged.code_sequence add column if not exists tenant_id uuid;
alter table ged.code_sequence add column if not exists entity_name text;
alter table ged.code_sequence add column if not exists prefix text;
update ged.code_sequence set prefix = 'COD' where prefix is null;
alter table ged.code_sequence alter column prefix set default 'COD';
alter table ged.code_sequence alter column prefix set not null;
alter table ged.code_sequence add column if not exists current_value bigint not null default 0;
alter table ged.code_sequence add column if not exists padding int not null default 4;
alter table ged.code_sequence add column if not exists created_at timestamptz not null default now();
alter table ged.code_sequence add column if not exists updated_at timestamptz null;
alter table ged.code_sequence add column if not exists reg_status char(1) not null default 'A';

create unique index if not exists ux_code_sequence_tenant_entity
on ged.code_sequence(tenant_id, entity_name)
where coalesce(reg_status,'A')='A';

create index if not exists ix_code_sequence_tenant
on ged.code_sequence(tenant_id);

create table if not exists ged.parameter_category (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    code text not null,
    name text not null,
    description text null,
    icon text null,
    display_order int not null default 0,
    is_system boolean not null default false,
    allow_hierarchy boolean not null default false,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);

alter table ged.parameter_category add column if not exists tenant_id uuid;
alter table ged.parameter_category add column if not exists code text;
alter table ged.parameter_category add column if not exists name text;
alter table ged.parameter_category add column if not exists description text null;
alter table ged.parameter_category add column if not exists icon text null;
alter table ged.parameter_category add column if not exists display_order int not null default 0;
alter table ged.parameter_category add column if not exists is_system boolean not null default false;
alter table ged.parameter_category add column if not exists allow_hierarchy boolean not null default false;
alter table ged.parameter_category add column if not exists is_active boolean not null default true;
alter table ged.parameter_category add column if not exists created_at timestamptz not null default now();
alter table ged.parameter_category add column if not exists updated_at timestamptz null;
alter table ged.parameter_category add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.parameter_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    category_id uuid not null,
    parent_id uuid null,
    code text not null,
    name text not null,
    description text null,
    abbreviation text null,
    external_code text null,
    color text null,
    icon text null,
    metadata_json jsonb null,
    display_order int not null default 0,
    is_default boolean not null default false,
    is_system boolean not null default false,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid null,
    updated_at timestamptz null,
    updated_by uuid null,
    reg_status char(1) not null default 'A'
);

alter table ged.parameter_item add column if not exists tenant_id uuid;
alter table ged.parameter_item add column if not exists category_id uuid;
alter table ged.parameter_item add column if not exists parent_id uuid null;
alter table ged.parameter_item add column if not exists code text;
alter table ged.parameter_item add column if not exists name text;
alter table ged.parameter_item add column if not exists description text null;
alter table ged.parameter_item add column if not exists abbreviation text null;
alter table ged.parameter_item add column if not exists external_code text null;
alter table ged.parameter_item add column if not exists color text null;
alter table ged.parameter_item add column if not exists icon text null;
alter table ged.parameter_item add column if not exists metadata_json jsonb null;
alter table ged.parameter_item add column if not exists display_order int not null default 0;
alter table ged.parameter_item add column if not exists is_default boolean not null default false;
alter table ged.parameter_item add column if not exists is_system boolean not null default false;
alter table ged.parameter_item add column if not exists is_active boolean not null default true;
alter table ged.parameter_item add column if not exists created_at timestamptz not null default now();
alter table ged.parameter_item add column if not exists created_by uuid null;
alter table ged.parameter_item add column if not exists updated_at timestamptz null;
alter table ged.parameter_item add column if not exists updated_by uuid null;
alter table ged.parameter_item add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.parameter_item_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    item_id uuid not null,
    category_code text null,
    action text not null,
    changed_by uuid null,
    old_data jsonb null,
    new_data jsonb null,
    reason text null,
    created_at timestamptz not null default now()
);

alter table ged.parameter_item_history add column if not exists tenant_id uuid;
alter table ged.parameter_item_history add column if not exists item_id uuid;
alter table ged.parameter_item_history add column if not exists category_code text null;
alter table ged.parameter_item_history add column if not exists action text;
alter table ged.parameter_item_history add column if not exists changed_by uuid null;
alter table ged.parameter_item_history add column if not exists old_data jsonb null;
alter table ged.parameter_item_history add column if not exists new_data jsonb null;
alter table ged.parameter_item_history add column if not exists reason text null;
alter table ged.parameter_item_history add column if not exists created_at timestamptz not null default now();

create unique index if not exists ux_parameter_category_tenant_code
on ged.parameter_category(tenant_id, code)
where coalesce(reg_status,'A')='A';

create unique index if not exists ux_parameter_item_tenant_category_code
on ged.parameter_item(tenant_id, category_id, code)
where coalesce(reg_status,'A')='A';

create index if not exists ix_parameter_item_tenant_category
on ged.parameter_item(tenant_id, category_id, display_order, name);



-- InovaGED - Contextual search and loan delivery (inlined; SQL puro, sem \i/\echo).
-- Smart contextual search, guided loans and secure shared delivery
create schema if not exists ged;
create extension if not exists pgcrypto;
-- unaccent is optional; application falls back to lower/ILIKE when unavailable.

create table if not exists ged.search_context_term (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    term text not null,
    normalized_term text not null,
    category text not null,
    synonyms text[] null,
    related_terms text[] null,
    weight numeric not null default 1,
    is_sensitive boolean not null default false,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create index if not exists ix_search_context_term_tenant_normalized on ged.search_context_term(tenant_id, normalized_term);
create index if not exists ix_search_context_term_tenant_category on ged.search_context_term(tenant_id, category);
create index if not exists ix_search_context_term_synonyms on ged.search_context_term using gin(synonyms);
create index if not exists ix_search_context_term_related_terms on ged.search_context_term using gin(related_terms);

insert into ged.search_context_term(tenant_id, term, normalized_term, category, synonyms, related_terms, weight, is_sensitive)
select t.id, s.term, lower(coalesce(s.normalized_term,s.term,'')), s.category, s.synonyms, s.related_terms, s.weight, s.is_sensitive
from (select distinct tenant_id as id from ged.document where tenant_id is not null union select distinct tenant_id from ged.app_user where tenant_id is not null union select '00000000-0000-0000-0000-000000000000'::uuid) t
cross join (values
('câncer de mama','cancer de mama','clinical',array['neoplasia mamária','carcinoma mamário','tumor de mama','CA mama','câncer mamário','cancer de mama','oncologia mama','mastologia'],array['mama','biópsia','quimioterapia','radioterapia','oncologia','mamografia'],3,true),
('diabetes','diabetes','clinical',array['diabete','DM','diabetes mellitus'],array['endocrinologia','glicemia','insulina'],2,true),
('AVC','avc','clinical',array['acidente vascular cerebral','derrame'],array['neurologia','tomografia','prontuário'],2,true),
('APAC','apac','administrative',array['autorização de procedimento','autorização de alta complexidade'],array['guia','autorização','oncologia'],2,false),
('ultrassom','ultrassom','exam',array['ultrassonografia','USG'],array['exame','laudo'],1.5,false),
('tomografia','tomografia','exam',array['TC','tomografia computadorizada'],array['exame','laudo'],1.5,false),
('prontuário','prontuario','document_type',array['registro do paciente','ficha do paciente'],array['paciente','histórico clínico'],1.5,true)
) as s(term, normalized_term, category, synonyms, related_terms, weight, is_sensitive)
where not exists (select 1 from ged.search_context_term x where x.tenant_id=t.id and x.normalized_term=lower(s.normalized_term) and x.reg_status='A');


do $$
begin
    if exists (select 1 from pg_type t join pg_namespace n on n.oid=t.typnamespace where n.nspname='ged' and t.typname='loan_status') then
        alter type ged.loan_status add value if not exists 'DRAFT';
        alter type ged.loan_status add value if not exists 'TRIAGE';
        alter type ged.loan_status add value if not exists 'NEEDS_INFO';
        alter type ged.loan_status add value if not exists 'PREPARING_PHYSICAL';
        alter type ged.loan_status add value if not exists 'WAITING_PICKUP';
        alter type ged.loan_status add value if not exists 'DIGITAL_LINK_SENT';
    end if;
end $$;

alter table if exists ged.loan_request add column if not exists request_no text null;
alter table if exists ged.loan_request add column if not exists request_type text not null default 'DOCUMENT_REQUEST';
alter table if exists ged.loan_request add column if not exists delivery_mode text not null default 'PHYSICAL';
alter table if exists ged.loan_request add column if not exists request_description text null;
alter table if exists ged.loan_request add column if not exists patient_name text null;
alter table if exists ged.loan_request add column if not exists medical_record_number text null;
alter table if exists ged.loan_request add column if not exists patient_identifier_masked text null;
alter table if exists ged.loan_request add column if not exists desired_due_at timestamptz null;
alter table if exists ged.loan_request add column if not exists sla_due_at timestamptz null;
alter table if exists ged.loan_request add column if not exists sla_hours int null;
alter table if exists ged.loan_request add column if not exists priority text not null default 'NORMAL';
alter table if exists ged.loan_request add column if not exists requester_contact text null;
alter table if exists ged.loan_request add column if not exists requester_sector_id uuid null;
alter table if exists ged.loan_request add column if not exists requester_sector_name text null;
alter table if exists ged.loan_request add column if not exists admin_response text null;
alter table if exists ged.loan_request add column if not exists admin_response_at timestamptz null;
alter table if exists ged.loan_request add column if not exists admin_response_by uuid null;
alter table if exists ged.loan_request add column if not exists delivery_instructions text null;
alter table if exists ged.loan_request add column if not exists digital_delivery_enabled boolean not null default false;
alter table if exists ged.loan_request add column if not exists physical_delivery_enabled boolean not null default false;
alter table if exists ged.loan_request add column if not exists secure_link_id uuid null;
alter table if exists ged.loan_request add column if not exists status_detail text null;
alter table if exists ged.loan_request add column if not exists last_message_at timestamptz null;
alter table if exists ged.loan_request add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.loan_request_item add column if not exists is_manual boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists requested_text text null;
alter table if exists ged.loan_request_item add column if not exists date_hint text null;
alter table if exists ged.loan_request_item add column if not exists context_terms text[] null;
alter table if exists ged.loan_request_item add column if not exists document_type text null;
alter table if exists ged.loan_request_item add column if not exists patient_name text null;
alter table if exists ged.loan_request_item add column if not exists medical_record_number text null;
alter table if exists ged.loan_request_item add column if not exists matched_document_id uuid null;
alter table if exists ged.loan_request_item add column if not exists matched_version_id uuid null;
alter table if exists ged.loan_request_item add column if not exists match_score numeric null;
alter table if exists ged.loan_request_item add column if not exists match_reason text null;
alter table if exists ged.loan_request_item add column if not exists digital_available boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists physical_available boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.loan_request_message (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, loan_request_id uuid not null,
    sender_user_id uuid null, sender_name text null, sender_role text null, message text not null,
    message_type text not null default 'COMMENT', is_internal boolean not null default false,
    created_at timestamptz not null default now(), reg_status char(1) not null default 'A');
create index if not exists ix_loan_request_message_request on ged.loan_request_message(tenant_id, loan_request_id, created_at desc);

create table if not exists ged.loan_sla_policy (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, code text not null, name text not null,
    delivery_mode text not null, priority text not null default 'NORMAL', sla_hours int not null,
    is_default boolean not null default false, created_at timestamptz not null default now(), reg_status char(1) not null default 'A');
create unique index if not exists ux_loan_sla_policy_tenant_code on ged.loan_sla_policy(tenant_id, code) where reg_status='A';
insert into ged.loan_sla_policy(tenant_id, code, name, delivery_mode, priority, sla_hours, is_default)
select t.id, v.code, v.name, v.delivery_mode, v.priority, v.sla_hours, v.is_default from (select distinct tenant_id as id from ged.app_user where tenant_id is not null) t cross join (values
('PHYSICAL_NORMAL','Físico normal','PHYSICAL','NORMAL',48,true),('PHYSICAL_URGENT','Físico urgente','PHYSICAL','URGENT',24,false),('DIGITAL_NORMAL','Digital normal','DIGITAL','NORMAL',24,true),('DIGITAL_URGENT','Digital urgente','DIGITAL','URGENT',8,false)) v(code,name,delivery_mode,priority,sla_hours,is_default)
on conflict do nothing;

create table if not exists ged.secure_document_link (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, loan_request_id uuid null, document_id uuid not null,
    version_id uuid null, token_hash text not null, expires_at timestamptz null, is_permanent boolean not null default false, max_access_count int null,
    access_count int not null default 0, allow_smart_search boolean not null default true, allow_download boolean not null default true,
    created_by uuid null, created_at timestamptz not null default now(), revoked_at timestamptz null, revoked_by uuid null,
    revoke_reason text null, reg_status char(1) not null default 'A');
create unique index if not exists ux_secure_document_link_token_hash on ged.secure_document_link(token_hash);
create index if not exists ix_secure_document_link_loan on ged.secure_document_link(tenant_id, loan_request_id);
create index if not exists ix_secure_document_link_document on ged.secure_document_link(tenant_id, document_id);
create index if not exists ix_secure_document_link_expires on ged.secure_document_link(expires_at);
create index if not exists ix_secure_document_link_revoked on ged.secure_document_link(revoked_at);

create table if not exists ged.secure_document_link_access (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, secure_link_id uuid not null,
    accessed_at timestamptz not null default now(), ip_address text null, user_agent text null, success boolean not null, reason text null);
create index if not exists ix_secure_document_link_access_link on ged.secure_document_link_access(tenant_id, secure_link_id, accessed_at desc);
-- Finalização idempotente da jornada Loans + link seguro de documento.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.secure_document_link (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid null,
    document_id uuid not null,
    version_id uuid null,
    token_hash text not null,
    public_url text null,
    title text null,
    description text null,
    recipient_name text null,
    recipient_contact text null,
    is_permanent boolean not null default false,
    expires_at timestamptz null,
    max_access_count int null,
    access_count int not null default 0,
    allow_preview boolean not null default true,
    allow_download boolean not null default false,
    allow_smart_search boolean not null default true,
    created_by uuid null,
    created_at timestamptz not null default now(),
    revoked_at timestamptz null,
    revoked_by uuid null,
    revoke_reason text null,
    last_access_at timestamptz null,
    reg_status char(1) not null default 'A'
);

alter table ged.secure_document_link add column if not exists tenant_id uuid not null;
alter table ged.secure_document_link add column if not exists loan_request_id uuid null;
alter table ged.secure_document_link add column if not exists document_id uuid not null;
alter table ged.secure_document_link add column if not exists version_id uuid null;
alter table ged.secure_document_link add column if not exists token_hash text not null;
alter table ged.secure_document_link add column if not exists public_url text null;
alter table ged.secure_document_link add column if not exists title text null;
alter table ged.secure_document_link add column if not exists description text null;
alter table ged.secure_document_link add column if not exists recipient_name text null;
alter table ged.secure_document_link add column if not exists recipient_contact text null;
alter table ged.secure_document_link add column if not exists is_permanent boolean not null default false;
alter table ged.secure_document_link add column if not exists expires_at timestamptz null;
alter table ged.secure_document_link add column if not exists max_access_count int null;
alter table ged.secure_document_link add column if not exists access_count int not null default 0;
alter table ged.secure_document_link add column if not exists allow_preview boolean not null default true;
alter table ged.secure_document_link add column if not exists allow_download boolean not null default false;
alter table ged.secure_document_link add column if not exists allow_smart_search boolean not null default true;
alter table ged.secure_document_link add column if not exists created_by uuid null;
alter table ged.secure_document_link add column if not exists created_at timestamptz not null default now();
alter table ged.secure_document_link add column if not exists revoked_at timestamptz null;
alter table ged.secure_document_link add column if not exists revoked_by uuid null;
alter table ged.secure_document_link add column if not exists revoke_reason text null;
alter table ged.secure_document_link add column if not exists last_access_at timestamptz null;
alter table ged.secure_document_link add column if not exists reg_status char(1) not null default 'A';

create unique index if not exists ux_secure_document_link_token_hash on ged.secure_document_link(token_hash);
create index if not exists ix_secure_document_link_token_hash on ged.secure_document_link(token_hash);
create index if not exists ix_secure_document_link_loan on ged.secure_document_link(tenant_id, loan_request_id);
create index if not exists ix_secure_document_link_document on ged.secure_document_link(tenant_id, document_id);
create index if not exists ix_secure_document_link_created on ged.secure_document_link(tenant_id, created_at desc);

create table if not exists ged.secure_document_link_access (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    secure_link_id uuid not null,
    accessed_at timestamptz not null default now(),
    ip_address text null,
    user_agent text null,
    success boolean not null,
    reason text null
);
create index if not exists ix_secure_document_link_access_link on ged.secure_document_link_access(tenant_id, secure_link_id, accessed_at desc);

alter table if exists ged.loan_request add column if not exists secure_link_id uuid null;
alter table if exists ged.loan_request add column if not exists digital_delivery_enabled boolean not null default false;
alter table if exists ged.loan_request add column if not exists physical_delivery_enabled boolean not null default false;
alter table if exists ged.loan_request add column if not exists admin_response text null;
alter table if exists ged.loan_request add column if not exists admin_response_at timestamptz null;
alter table if exists ged.loan_request add column if not exists admin_response_by uuid null;
alter table if exists ged.loan_request add column if not exists delivery_instructions text null;
alter table if exists ged.loan_request add column if not exists sla_due_at timestamptz null;
alter table if exists ged.loan_request add column if not exists sla_hours int null;
alter table if exists ged.loan_request add column if not exists status_detail text null;
alter table if exists ged.loan_request add column if not exists last_message_at timestamptz null;

alter table if exists ged.loan_request_item add column if not exists matched_document_id uuid null;
alter table if exists ged.loan_request_item add column if not exists matched_version_id uuid null;
alter table if exists ged.loan_request_item add column if not exists match_score numeric null;
alter table if exists ged.loan_request_item add column if not exists match_reason text null;
alter table if exists ged.loan_request_item add column if not exists digital_available boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists physical_available boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists physical_location text null;
alter table if exists ged.loan_request_item add column if not exists box_code text null;
alter table if exists ged.loan_request_item add column if not exists is_manual boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists requested_text text null;

create table if not exists ged.loan_request_message (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid not null,
    sender_user_id uuid null,
    sender_name text null,
    sender_role text null,
    message text not null,
    message_type text not null default 'COMMENT',
    is_internal boolean not null default false,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create index if not exists ix_loan_request_message_request_created on ged.loan_request_message(tenant_id, loan_request_id, created_at);
create schema if not exists ged;

create table if not exists ged.secure_document_link (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid null,
    document_id uuid not null,
    version_id uuid null,
    token_hash text not null,
    public_url text null,
    title text null,
    description text null,
    recipient_name text null,
    recipient_contact text null,
    is_permanent boolean not null default false,
    expires_at timestamptz null,
    max_access_count int null,
    access_count int not null default 0,
    allow_preview boolean not null default true,
    allow_download boolean not null default false,
    allow_smart_search boolean not null default true,
    created_by uuid null,
    created_at timestamptz not null default now(),
    last_access_at timestamptz null,
    revoked_at timestamptz null,
    revoked_by uuid null,
    revoke_reason text null,
    reg_status char(1) not null default 'A'
);

alter table ged.secure_document_link add column if not exists public_url text null;
alter table ged.secure_document_link add column if not exists title text null;
alter table ged.secure_document_link add column if not exists description text null;
alter table ged.secure_document_link add column if not exists recipient_name text null;
alter table ged.secure_document_link add column if not exists recipient_contact text null;
alter table ged.secure_document_link add column if not exists allow_preview boolean not null default true;
alter table ged.secure_document_link add column if not exists allow_download boolean not null default false;
alter table ged.secure_document_link add column if not exists allow_smart_search boolean not null default true;
alter table ged.secure_document_link add column if not exists is_permanent boolean not null default false;
alter table ged.secure_document_link add column if not exists expires_at timestamptz null;
alter table ged.secure_document_link add column if not exists max_access_count int null;
alter table ged.secure_document_link add column if not exists access_count int not null default 0;
alter table ged.secure_document_link add column if not exists last_access_at timestamptz null;
alter table ged.secure_document_link add column if not exists revoked_at timestamptz null;
alter table ged.secure_document_link add column if not exists revoked_by uuid null;
alter table ged.secure_document_link add column if not exists revoke_reason text null;
alter table ged.secure_document_link add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.secure_document_link_access (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    secure_link_id uuid not null,
    accessed_at timestamptz not null default now(),
    ip_address text null,
    user_agent text null,
    success boolean not null default true,
    reason text null
);

create unique index if not exists ux_secure_document_link_token_hash
on ged.secure_document_link(token_hash)
where coalesce(reg_status,'A')='A';

create index if not exists ix_secure_document_link_document
on ged.secure_document_link(tenant_id, document_id, created_at desc);

create index if not exists ix_secure_document_link_loan
on ged.secure_document_link(tenant_id, loan_request_id, created_at desc);

create index if not exists ix_secure_document_link_access_link
on ged.secure_document_link_access(tenant_id, secure_link_id, accessed_at desc);
