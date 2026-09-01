begin;
create schema if not exists ged;
create extension if not exists pgcrypto;
create table if not exists ged.print_branding_profile (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, profile_code varchar(80) not null, profile_name text not null,
 client_name text, contract_name text, organization_name text, primary_logo_asset_id uuid references ged.brand_asset(id), secondary_logo_asset_id uuid references ged.brand_asset(id),
 header_title text, header_subtitle text, header_extra_line text, footer_text text, footer_extra_line text,
 show_generated_at boolean not null default true, show_page_number boolean not null default true, show_protocol_info boolean not null default false,
 logo_position varchar(40) not null default 'TOP_LEFT', secondary_logo_position varchar(40) not null default 'TOP_RIGHT', primary_logo_width_mm numeric(8,2) not null default 38,
 secondary_logo_width_mm numeric(8,2) not null default 28, preserve_logo_aspect_ratio boolean not null default true, paper_size varchar(40) not null default 'A4', orientation varchar(40) not null default 'PORTRAIT',
 margin_top_mm numeric(8,2) not null default 10, margin_right_mm numeric(8,2) not null default 10, margin_bottom_mm numeric(8,2) not null default 10, margin_left_mm numeric(8,2) not null default 10,
 status varchar(40) not null default 'ACTIVE', is_default boolean not null default false, created_by uuid, created_by_name text, created_at timestamptz not null default now(), updated_by uuid, updated_by_name text,
 updated_at timestamptz, archived_by uuid, archived_at timestamptz, archive_reason text, reg_status char(1) not null default 'A');
create table if not exists ged.print_branding_binding (id uuid primary key default gen_random_uuid(),tenant_id uuid not null,binding_context varchar(60) not null,binding_key text not null,profile_id uuid not null references ged.print_branding_profile(id),enabled boolean not null default true,created_by uuid,created_at timestamptz not null default now(),updated_at timestamptz,reg_status char(1) not null default 'A');
create table if not exists ged.print_branding_audit_event (id uuid primary key default gen_random_uuid(),tenant_id uuid not null,profile_id uuid references ged.print_branding_profile(id),event_type varchar(80) not null,title text not null,description text,performed_by uuid,performed_by_name text,occurred_at timestamptz not null default now(),payload_json jsonb,reg_status char(1) not null default 'A');
create unique index if not exists ux_print_branding_profile_code on ged.print_branding_profile(tenant_id,profile_code) where reg_status='A';
create index if not exists ix_print_branding_profile_default on ged.print_branding_profile(tenant_id,is_default,status) where reg_status='A';
create unique index if not exists ux_print_branding_binding on ged.print_branding_binding(tenant_id,binding_context,binding_key) where reg_status='A';
create index if not exists ix_print_branding_audit_profile on ged.print_branding_audit_event(tenant_id,profile_id,occurred_at desc) where reg_status='A';
commit;
