create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.label_print_job (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, job_number varchar(40) not null,
 print_mode varchar(30) not null, template_code varchar(80) not null, template_name text null,
 subject_type varchar(40) not null, subject_id uuid null, batch_id uuid null, control_number text null,
 location text null, copies integer not null default 1 check (copies > 0), status varchar(30) not null default 'PENDING',
 payload_json jsonb not null, pdf_path text null, error_message text null, requested_by uuid null,
 requested_at timestamptz not null default now(), printed_by uuid null, printed_at timestamptz null,
 cancelled_by uuid null, cancelled_at timestamptz null, cancel_reason text null, reprint_reason text null,
 requested_ip inet null, requested_user_agent text null, reg_status char(1) not null default 'A'
);
alter table ged.label_print_job add column if not exists requested_ip inet null;
alter table ged.label_print_job add column if not exists requested_user_agent text null;

create table if not exists ged.label_print_job_item (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, job_id uuid not null references ged.label_print_job(id),
 subject_type varchar(40) not null, subject_id uuid null, control_number text null, location text null,
 payload_json jsonb not null, status varchar(30) not null default 'PENDING', printed_at timestamptz null,
 error_message text null, display_order integer not null default 0, reg_status char(1) not null default 'A'
);
create table if not exists ged.label_print_calibration (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, template_code varchar(80) not null,
 printer_name text null, page_size varchar(20) not null default 'A4', margin_top_mm numeric(10,2) not null default 0,
 margin_left_mm numeric(10,2) not null default 0, scale_percent numeric(10,2) not null default 100,
 label_width_mm numeric(10,2) null, label_height_mm numeric(10,2) null, gap_x_mm numeric(10,2) not null default 0,
 gap_y_mm numeric(10,2) not null default 0, labels_per_page integer not null default 1,
 created_by uuid null, created_at timestamptz not null default now(), updated_by uuid null, updated_at timestamptz null,
 reg_status char(1) not null default 'A'
);
create unique index if not exists ux_label_print_job_tenant_number on ged.label_print_job(tenant_id, job_number);
create index if not exists ix_label_print_job_tenant_status on ged.label_print_job(tenant_id, status, requested_at desc);
create index if not exists ix_label_print_job_template on ged.label_print_job(tenant_id, template_code, requested_at desc);
create index if not exists ix_label_print_job_control on ged.label_print_job(tenant_id, control_number);
create index if not exists ix_label_print_job_item_job on ged.label_print_job_item(tenant_id, job_id, display_order);
create unique index if not exists ux_label_print_calibration_tenant_template_printer
 on ged.label_print_calibration(tenant_id, template_code, coalesce(printer_name, 'DEFAULT')) where reg_status='A';

alter table if exists ged.label_print_history add column if not exists print_job_id uuid null;
alter table if exists ged.label_print_history add column if not exists print_job_item_id uuid null;
alter table if exists ged.label_print_history add column if not exists print_mode varchar(30) null;
alter table if exists ged.label_print_history add column if not exists generated_path text null;
