create schema if not exists ged;

create extension if not exists pgcrypto;

create table if not exists ged.classification_plan (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    parent_id uuid null,
    code text null,
    title text null,
    description text null,
    final_destination text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

alter table ged.classification_plan add column if not exists tenant_id uuid;
alter table ged.classification_plan add column if not exists parent_id uuid;
alter table ged.classification_plan add column if not exists code text;
alter table ged.classification_plan add column if not exists title text;
alter table ged.classification_plan add column if not exists description text;
alter table ged.classification_plan add column if not exists final_destination text;
alter table ged.classification_plan add column if not exists created_at timestamptz not null default now();
alter table ged.classification_plan add column if not exists reg_status char(1) not null default 'A';

update ged.classification_plan
set title = coalesce(nullif(title, ''), nullif(description, ''), nullif(code, ''), 'Sem título')
where title is null or title = '';

create index if not exists ix_classification_plan_tenant_status
on ged.classification_plan(tenant_id, reg_status);

create index if not exists ix_classification_plan_tenant_code
on ged.classification_plan(tenant_id, code);
