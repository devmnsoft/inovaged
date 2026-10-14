-- Entrada por URL, identidade confirmada e emissão atômica das aberturas.
-- Não reescreve concessões antigas, catálogo nem personalizações.

alter table ged.sector_patient add column if not exists confirmed_at timestamptz;
alter table ged.sector_patient add column if not exists canonical_patient_id uuid;

alter table ged.sector_encounter add column if not exists confirmed_at timestamptz;

create table if not exists ged.sector_patient_alias (
    id uuid primary key,
    tenant_id uuid not null,
    patient_id uuid not null,
    source_system text not null,
    source_unit text not null default '',
    identifier_kind text not null,
    external_code text not null,
    confirmed_at timestamptz not null default now(),
    unique (tenant_id, source_system, source_unit, identifier_kind, external_code)
);

create table if not exists ged.sector_entry_ticket (
    id uuid primary key,
    integration_id uuid not null,
    tenant_id uuid not null,
    identifier_kind text not null,
    identifier_value text not null,
    encounter_code text not null,
    document_name text,
    document_code text,
    document_id uuid,
    version_id uuid,
    mode text not null,
    created_at timestamptz not null default now(),
    expires_at timestamptz not null,
    consumed_at timestamptz
);

create table if not exists ged.sector_open_request (
    id uuid primary key,
    tenant_id uuid not null,
    integration_id uuid not null,
    idempotency_key text,
    correlation_id text not null,
    page_code_hash char(64) not null,
    frame_code_hash char(64) not null,
    created_at timestamptz not null default now(),
    unique (integration_id, idempotency_key)
);

alter table ged.sector_open_code add column if not exists granted_catalog text[];
alter table ged.sector_open_code add column if not exists created_at timestamptz not null default now();
alter table ged.sector_open_session add column if not exists granted_catalog text[];
