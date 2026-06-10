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
