create schema if not exists ged;
create extension if not exists pgcrypto;

do $$
begin
    if not exists (
        select 1 from pg_type t join pg_namespace n on n.oid = t.typnamespace
        where n.nspname = 'ged' and t.typname = 'instrument_type'
    ) then
        create type ged.instrument_type as enum ('PCD','TTD','POP');
    end if;
end $$;

create table if not exists ged.instrument_version (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    instrument_type ged.instrument_type not null,
    version_no integer not null default 1,
    is_published boolean not null default false,
    published_at timestamptz null,
    published_by uuid null,
    notes text null,
    reg_date timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

alter table ged.instrument_version add column if not exists tenant_id uuid;
alter table ged.instrument_version add column if not exists instrument_type ged.instrument_type;
alter table ged.instrument_version add column if not exists version_no integer not null default 1;
alter table ged.instrument_version add column if not exists is_published boolean not null default false;
alter table ged.instrument_version add column if not exists published_at timestamptz null;
alter table ged.instrument_version add column if not exists published_by uuid null;
alter table ged.instrument_version add column if not exists notes text null;
alter table ged.instrument_version add column if not exists reg_date timestamptz not null default now();
alter table ged.instrument_version add column if not exists reg_status char(1) not null default 'A';

update ged.instrument_version set is_published = true
where published_at is not null and coalesce(is_published,false) = false;

create index if not exists ix_instrument_version_tenant_type
on ged.instrument_version(tenant_id, instrument_type, version_no desc);

create index if not exists ix_instrument_version_published
on ged.instrument_version(tenant_id, instrument_type, is_published, published_at desc)
where coalesce(reg_status,'A')='A';
