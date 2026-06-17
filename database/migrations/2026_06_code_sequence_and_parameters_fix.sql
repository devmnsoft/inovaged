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
