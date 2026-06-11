-- InovaGED - Módulo Protocolo consolidado (idempotente)
-- Cria solicitações de protocolo, itens GED/manuais, anexos, histórico e vínculo com empréstimos.

CREATE EXTENSION IF NOT EXISTS pgcrypto;
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
    title text not null,
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
ALTER TABLE ged.protocol_request ADD COLUMN IF NOT EXISTS title text;
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
