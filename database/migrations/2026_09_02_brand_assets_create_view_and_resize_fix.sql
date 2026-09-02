create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.brand_asset (
 id uuid primary key default gen_random_uuid(), tenant_id uuid null,
 brand_name text not null, asset_name text not null, asset_type varchar(40) not null default 'LOGO',
 original_file_name text not null, stored_file_name text not null, content_type text not null,
 file_extension varchar(20) not null, file_size_bytes bigint not null default 0,
 file_hash_sha256 text not null, storage_relative_path text not null,
 width_px integer null, height_px integer null, default_width_mm numeric(8,2) not null default 38,
 default_height_mm numeric(8,2) null, preserve_aspect_ratio boolean not null default true,
 fit_mode varchar(40) not null default 'CONTAIN', default_position varchar(40) not null default 'TOP_LEFT',
 alt_text text null, is_default boolean not null default false, is_system_asset boolean not null default false,
 status varchar(40) not null default 'ACTIVE', created_by uuid null, created_by_name text null,
 created_at timestamptz not null default now(), archived_by uuid null, archived_at timestamptz null,
 archive_reason text null, reg_status char(1) not null default 'A'
);
alter table if exists ged.brand_asset
 add column if not exists default_width_mm numeric(8,2) not null default 38,
 add column if not exists default_height_mm numeric(8,2) null,
 add column if not exists preserve_aspect_ratio boolean not null default true,
 add column if not exists fit_mode varchar(40) not null default 'CONTAIN',
 add column if not exists default_position varchar(40) not null default 'TOP_LEFT',
 add column if not exists alt_text text null,
 add column if not exists notes text null,
 add column if not exists public_route text null,
 add column if not exists updated_at timestamptz null;

create table if not exists ged.print_logo_selection (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 context varchar(60) not null, context_key text not null,
 logo_asset_id uuid null references ged.brand_asset(id), width_mm numeric(8,2) not null default 38,
 height_mm numeric(8,2) null, preserve_aspect_ratio boolean not null default true,
 fit_mode varchar(40) not null default 'CONTAIN', position varchar(40) not null default 'TOP_LEFT',
 offset_x_mm numeric(8,2) not null default 0, offset_y_mm numeric(8,2) not null default 0,
 enabled boolean not null default true, created_by uuid null, created_at timestamptz not null default now(),
 updated_at timestamptz null, reg_status char(1) not null default 'A'
);
alter table if exists ged.print_logo_selection
 add column if not exists offset_x_mm numeric(8,2) not null default 0,
 add column if not exists offset_y_mm numeric(8,2) not null default 0;
create unique index if not exists ux_print_logo_selection_context
 on ged.print_logo_selection(tenant_id, context, context_key) where reg_status = 'A';
