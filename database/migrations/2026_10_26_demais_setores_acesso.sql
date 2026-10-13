-- Amplia Demais setores: identificador CPF/ID, concessão por origem, integração e revogação.
-- Não reescreve o seed do catálogo nem personalizações.

alter table ged.sector_patient add column if not exists identifier_kind text not null default 'ID';

do $uniq$
declare
    old_name name;
begin
    if to_regclass('ged.sector_patient') is null then
        return;
    end if;
    if exists (
        select 1 from pg_constraint
        where conname = 'ux_sector_patient_identity' and conrelid = 'ged.sector_patient'::regclass
    ) then
        return;
    end if;
    select c.conname into old_name
    from pg_constraint c
    where c.conrelid = 'ged.sector_patient'::regclass
      and c.contype = 'u'
      and pg_get_constraintdef(c.oid) like 'UNIQUE (tenant_id, source_system, source_unit, external_code)%';
    if old_name is not null then
        execute format('alter table ged.sector_patient drop constraint %I', old_name);
    end if;
    alter table ged.sector_patient
        add constraint ux_sector_patient_identity
        unique (tenant_id, source_system, source_unit, identifier_kind, external_code);
end
$uniq$;

alter table ged.sector_embed_grant add column if not exists document_id uuid;
alter table ged.sector_embed_grant add column if not exists origin text;

create table if not exists ged.sector_document_synonym (
    tenant_id uuid not null,
    catalog_code text not null,
    synonym text not null,
    unique (tenant_id, synonym)
);

create table if not exists ged.sector_integration (
    id uuid primary key,
    tenant_id uuid not null,
    client_id text not null,
    secret_hash char(64) not null,
    source_system text not null,
    source_unit text not null default '',
    origins text[] not null default '{}',
    catalog_codes text[] not null default '{}',
    allow_encounter_list boolean not null default false,
    max_minutes integer not null default 15,
    active boolean not null default true,
    unique (tenant_id, client_id)
);

create table if not exists ged.sector_open_session (
    id uuid primary key,
    tenant_id uuid not null,
    integration_id uuid not null,
    cookie_hash char(64) not null unique,
    patient_id uuid not null,
    encounter_id uuid not null,
    catalog_code text,
    document_id uuid,
    version_id uuid,
    mode text not null,
    origin text not null,
    correlation_id text not null,
    expires_at timestamptz not null,
    revoked_at timestamptz,
    created_at timestamptz not null default now()
);

create table if not exists ged.sector_open_code (
    id uuid primary key,
    tenant_id uuid not null,
    code_hash char(64) not null unique,
    session_id uuid,
    patient_id uuid not null,
    encounter_id uuid not null,
    catalog_code text,
    document_id uuid,
    version_id uuid,
    mode text not null,
    origin text not null,
    integration_id uuid not null,
    correlation_id text not null,
    expires_at timestamptz not null,
    consumed_at timestamptz
);
