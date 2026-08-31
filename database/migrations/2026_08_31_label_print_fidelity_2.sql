begin;
create schema if not exists ged;
create extension if not exists pgcrypto;
create table if not exists ged.label_print_profile (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, profile_name text not null,
 printer_name text null, paper_size varchar(40) not null default 'A4', orientation varchar(40) not null default 'PORTRAIT',
 margin_top_mm numeric(8,2) not null default 0 check(margin_top_mm between 0 and 30), margin_left_mm numeric(8,2) not null default 0 check(margin_left_mm between 0 and 30),
 offset_x_mm numeric(8,2) not null default 0 check(offset_x_mm between -20 and 20), offset_y_mm numeric(8,2) not null default 0 check(offset_y_mm between -20 and 20),
 scale_percent numeric(8,2) not null default 100 check(scale_percent between 80 and 120), label_gap_x_mm numeric(8,2) not null default 0, label_gap_y_mm numeric(8,2) not null default 0,
 is_default boolean not null default false, notes text null, created_by uuid null, created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A');
create table if not exists ged.label_print_quality_issue (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, template_code text not null, profile_id uuid null references ged.label_print_profile(id), issue_type varchar(80) not null,
 severity varchar(30) not null default 'MEDIUM', title text not null, description text null, recommended_action text null, status varchar(40) not null default 'OPEN', created_at timestamptz not null default now(), reg_status char(1) not null default 'A');
create index if not exists ix_label_print_profile_tenant on ged.label_print_profile(tenant_id,is_default,created_at desc) where reg_status='A';
create unique index if not exists ux_label_print_profile_default on ged.label_print_profile(tenant_id) where is_default and reg_status='A';
create index if not exists ix_label_print_quality_issue_template on ged.label_print_quality_issue(tenant_id,template_code,severity,status) where reg_status='A';
commit;
