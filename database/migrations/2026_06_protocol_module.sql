-- InovaGED - Módulo Protocolo ponta a ponta (idempotente)
CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS ged.schema_migration_history (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    script_name text NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now(),
    applied_by text NULL,
    checksum_sha256 text NULL,
    success boolean NOT NULL DEFAULT true,
    notes text NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_schema_migration_history_script ON ged.schema_migration_history(script_name);


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
