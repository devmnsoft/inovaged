create schema if not exists ged;

create extension if not exists pgcrypto;

create table if not exists ged.classification_plan_version (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    version_no integer null,
    title text not null default '',
    notes text null,
    published_by uuid null,
    published_at timestamptz null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

alter table ged.classification_plan_version add column if not exists tenant_id uuid;
alter table ged.classification_plan_version add column if not exists version_no integer;
alter table ged.classification_plan_version add column if not exists title text not null default '';
alter table ged.classification_plan_version add column if not exists notes text;
alter table ged.classification_plan_version add column if not exists published_by uuid;
alter table ged.classification_plan_version add column if not exists published_at timestamptz;
alter table ged.classification_plan_version add column if not exists created_at timestamptz not null default now();
alter table ged.classification_plan_version add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.document add column if not exists classification_id uuid;
alter table if exists ged.document add column if not exists retention_basis_at timestamptz null;
alter table if exists ged.document add column if not exists retention_due_at timestamptz null;
alter table if exists ged.document add column if not exists retention_status text null;

create index if not exists ix_classification_plan_version_tenant_published
on ged.classification_plan_version(tenant_id, published_at desc, created_at desc)
where coalesce(reg_status, 'A') = 'A';
