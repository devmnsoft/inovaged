create schema if not exists ged;
create extension if not exists pgcrypto;

alter table if exists ged.print_logo_selection
    add column if not exists position_x_mm numeric(8,2) not null default 0,
    add column if not exists position_y_mm numeric(8,2) not null default 0,
    add column if not exists z_index integer not null default 1,
    add column if not exists show_in_preview boolean not null default true,
    add column if not exists show_in_print boolean not null default true,
    add column if not exists apply_to_all_copies boolean not null default true,
    add column if not exists notes text null;

create table if not exists ged.print_logo_layout_validation (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
    context varchar(60) not null, context_key text not null,
    logo_asset_id uuid null references ged.brand_asset(id), validation_type varchar(80) not null,
    severity varchar(30) not null default 'MEDIUM', title text not null,
    description text null, recommended_action text null, status varchar(40) not null default 'OPEN',
    created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create index if not exists ix_print_logo_layout_validation_context on ged.print_logo_layout_validation(tenant_id,context,context_key,severity,status) where reg_status='A';
