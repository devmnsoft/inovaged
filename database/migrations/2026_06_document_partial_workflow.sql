-- InovaGED - Documentos fracionados/incompletos.
-- Idempotente: pode ser executada várias vezes sem erro.

CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE ged.document_version
    ADD COLUMN IF NOT EXISTS is_partial_document boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS partial_group_id uuid NULL,
    ADD COLUMN IF NOT EXISTS partial_part_number int NULL,
    ADD COLUMN IF NOT EXISTS partial_total_parts int NULL,
    ADD COLUMN IF NOT EXISTS partial_status text NOT NULL DEFAULT 'NOT_PARTIAL',
    ADD COLUMN IF NOT EXISTS consolidated_version_id uuid NULL,
    ADD COLUMN IF NOT EXISTS uploaded_at_utc timestamptz NULL;

-- Compatibilidade com schema legado já usado por telas antigas.
ALTER TABLE ged.document_version
    ADD COLUMN IF NOT EXISTS is_document_incomplete boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS part_number int NULL,
    ADD COLUMN IF NOT EXISTS total_parts int NULL;

UPDATE ged.document_version
SET is_partial_document = COALESCE(is_partial_document, false),
    is_document_incomplete = COALESCE(is_document_incomplete, false),
    partial_status = COALESCE(NULLIF(partial_status, ''), CASE WHEN COALESCE(is_document_incomplete, false) THEN 'INCOMPLETE' ELSE 'NOT_PARTIAL' END),
    uploaded_at_utc = COALESCE(uploaded_at_utc, created_at, now())
WHERE is_partial_document IS NULL
   OR is_document_incomplete IS NULL
   OR partial_status IS NULL
   OR partial_status = ''
   OR uploaded_at_utc IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_document_version_partial_status'
          AND conrelid = 'ged.document_version'::regclass
    ) THEN
        ALTER TABLE ged.document_version
            ADD CONSTRAINT ck_document_version_partial_status
            CHECK (partial_status IN ('NOT_PARTIAL','INCOMPLETE','COMPLETE','CONSOLIDATED','CANCELLED'));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS ged.document_partial_part (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    document_id uuid not null,
    version_id uuid not null,
    partial_group_id uuid not null,
    part_number int not null,
    total_parts int null,
    file_name text null,
    size_bytes bigint null,
    uploaded_at_utc timestamptz not null default now(),
    uploaded_by uuid null,
    status text not null default 'UPLOADED',
    notes text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

ALTER TABLE ged.document_partial_part
    ADD COLUMN IF NOT EXISTS tenant_id uuid,
    ADD COLUMN IF NOT EXISTS document_id uuid,
    ADD COLUMN IF NOT EXISTS version_id uuid,
    ADD COLUMN IF NOT EXISTS partial_group_id uuid,
    ADD COLUMN IF NOT EXISTS part_number int,
    ADD COLUMN IF NOT EXISTS total_parts int NULL,
    ADD COLUMN IF NOT EXISTS file_name text NULL,
    ADD COLUMN IF NOT EXISTS size_bytes bigint NULL,
    ADD COLUMN IF NOT EXISTS uploaded_at_utc timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS uploaded_by uuid NULL,
    ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'UPLOADED',
    ADD COLUMN IF NOT EXISTS notes text NULL,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ux_document_partial_part_group_part_active'
          AND conrelid = 'ged.document_partial_part'::regclass
    ) THEN
        ALTER TABLE ged.document_partial_part
            ADD CONSTRAINT ux_document_partial_part_group_part_active
            UNIQUE (tenant_id, partial_group_id, part_number);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ux_document_partial_part_version'
          AND conrelid = 'ged.document_partial_part'::regclass
    ) THEN
        ALTER TABLE ged.document_partial_part
            ADD CONSTRAINT ux_document_partial_part_version
            UNIQUE (tenant_id, version_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_document_partial_part_tenant_document ON ged.document_partial_part (tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_tenant_group_part ON ged.document_partial_part (tenant_id, partial_group_id, part_number);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_tenant_status ON ged.document_partial_part (tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_group ON ged.document_partial_part (partial_group_id);
CREATE INDEX IF NOT EXISTS ix_document_partial_part_version ON ged.document_partial_part (version_id);
CREATE INDEX IF NOT EXISTS ix_document_version_partial_group_id ON ged.document_version (partial_group_id);
CREATE INDEX IF NOT EXISTS ix_document_version_partial_status ON ged.document_version (partial_status);
CREATE INDEX IF NOT EXISTS ix_document_version_uploaded_at_utc ON ged.document_version (uploaded_at_utc);

-- Permissões funcionais.
CREATE TABLE IF NOT EXISTS ged.permission (
    code text primary key,
    name text null,
    description text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

INSERT INTO ged.permission (code, name, description)
VALUES
    ('DOCUMENT_PART_MARK_INCOMPLETE', 'Marcar documento incompleto', 'Permite marcar documento completo como Documento incompleto / fracionado.'),
    ('DOCUMENT_PART_ADD', 'Adicionar parte de documento', 'Permite anexar partes a documentos fracionados.'),
    ('DOCUMENT_PART_VIEW', 'Ver partes de documento', 'Permite visualizar histórico e partes de documentos fracionados.'),
    ('DOCUMENT_PART_CONSOLIDATE', 'Consolidar documento fracionado', 'Permite consolidar logicamente documentos com partes completas.'),
    ('DOCUMENT_PART_CANCEL', 'Cancelar fracionamento', 'Permite cancelar o fluxo de documento fracionado sem apagar arquivos.')
ON CONFLICT (code) DO UPDATE
SET name = EXCLUDED.name,
    description = EXCLUDED.description,
    reg_status = 'A';

-- Eventos de auditoria específicos, quando o enum existir.
DO $$
DECLARE
    enum_exists boolean;
    action_value text;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'ged' AND t.typname = 'audit_action_enum'
    ) INTO enum_exists;

    IF enum_exists THEN
        FOREACH action_value IN ARRAY ARRAY[
            'DOCUMENT_MARK_INCOMPLETE',
            'DOCUMENT_PART_MARK_INCOMPLETE',
            'DOCUMENT_PART_CREATE',
            'DOCUMENT_PART_UPLOAD',
            'DOCUMENT_PART_VIEW',
            'DOCUMENT_PART_CONSOLIDATE',
            'DOCUMENT_PART_CANCEL',
            'DOCUMENT_PART_COMPLETE',
            'DOCUMENT_PART_PREVIEW',
            'DOCUMENT_PART_DOWNLOAD'
        ] LOOP
            IF NOT EXISTS (
                SELECT 1 FROM pg_enum e
                JOIN pg_type t ON t.oid = e.enumtypid
                JOIN pg_namespace n ON n.oid = t.typnamespace
                WHERE n.nspname = 'ged'
                  AND t.typname = 'audit_action_enum'
                  AND e.enumlabel = action_value
            ) THEN
                EXECUTE format('ALTER TYPE ged.audit_action_enum ADD VALUE %L', action_value);
            END IF;
        END LOOP;
    END IF;
END $$;
