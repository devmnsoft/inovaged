create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.brand_asset (
 id uuid primary key default gen_random_uuid(), tenant_id uuid null, brand_name text not null, asset_name text not null,
 asset_type varchar(40) not null default 'LOGO', original_file_name text not null, stored_file_name text not null,
 content_type text not null, file_extension varchar(20) not null, file_size_bytes bigint not null default 0,
 file_hash_sha256 text not null, storage_relative_path text not null, public_route text null, width_px integer null,
 height_px integer null, is_default boolean not null default false, is_system_asset boolean not null default false,
 status varchar(40) not null default 'ACTIVE', created_by uuid null, created_by_name text null,
 created_at timestamptz not null default now(), archived_by uuid null, archived_at timestamptz null,
 archive_reason text null, reg_status char(1) not null default 'A');
create table if not exists ged.print_brand_profile (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, profile_name text not null,
 primary_logo_asset_id uuid null references ged.brand_asset(id), secondary_logo_asset_id uuid null references ged.brand_asset(id),
 header_title text null, header_subtitle text null, footer_text text null, logo_position varchar(40) not null default 'TOP_LEFT',
 logo_width_mm numeric(8,2) not null default 38, logo_height_mm numeric(8,2) null, preserve_aspect_ratio boolean not null default true,
 fit_mode varchar(40) not null default 'CONTAIN', is_default boolean not null default false, status varchar(40) not null default 'ACTIVE',
 created_by uuid null, created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A');
create table if not exists ged.print_template_brand_binding (
 id uuid primary key default gen_random_uuid(), tenant_id uuid null, template_code text not null, print_context varchar(40) not null default 'LABEL',
 brand_profile_id uuid null references ged.print_brand_profile(id), logo_asset_id uuid null references ged.brand_asset(id), logo_slot varchar(40) not null default 'PRIMARY',
 enabled boolean not null default true, created_by uuid null, created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A');
create index if not exists ix_brand_asset_tenant_status on ged.brand_asset(tenant_id,status,created_at desc) where reg_status='A';
create index if not exists ix_brand_asset_hash on ged.brand_asset(tenant_id,file_hash_sha256) where reg_status='A';
create index if not exists ix_print_brand_profile_tenant on ged.print_brand_profile(tenant_id,is_default,status) where reg_status='A';
create unique index if not exists ux_print_template_brand_binding on ged.print_template_brand_binding(coalesce(tenant_id,'00000000-0000-0000-0000-000000000000'::uuid),template_code,print_context,logo_slot) where reg_status='A';
