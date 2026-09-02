create schema if not exists ged;
create extension if not exists pgcrypto;

alter table if exists ged.brand_asset
 add column if not exists default_width_mm numeric(8,2) not null default 38,
 add column if not exists default_height_mm numeric(8,2) null,
 add column if not exists preserve_aspect_ratio boolean not null default true,
 add column if not exists fit_mode varchar(40) not null default 'CONTAIN',
 add column if not exists default_position varchar(40) not null default 'TOP_LEFT',
 add column if not exists alt_text text null,
 add column if not exists notes text null,
 add column if not exists updated_at timestamptz null;

create table if not exists ged.print_logo_selection (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 context varchar(60) not null, context_key text not null,
 logo_asset_id uuid null references ged.brand_asset(id),
 width_mm numeric(8,2) not null default 38, height_mm numeric(8,2) null,
 preserve_aspect_ratio boolean not null default true, fit_mode varchar(40) not null default 'CONTAIN',
 position varchar(40) not null default 'TOP_LEFT', margin_top_mm numeric(8,2) not null default 0,
 margin_left_mm numeric(8,2) not null default 0, enabled boolean not null default true,
 created_by uuid null, created_at timestamptz not null default now(), updated_at timestamptz null,
 reg_status char(1) not null default 'A',
 constraint ck_print_logo_width check(width_mm between 10 and 90),
 constraint ck_print_logo_height check(height_mm is null or height_mm between 5 and 60),
 constraint ck_print_logo_fit check(fit_mode in ('CONTAIN','COVER','FILL'))
);
create unique index if not exists ux_print_logo_selection_context on ged.print_logo_selection(tenant_id,context,context_key) where reg_status='A';
