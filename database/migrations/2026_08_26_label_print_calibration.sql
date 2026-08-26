-- Labels Professional Print Closing: calibração por tenant, usuário e impressora.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.label_print_calibration (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
    template_code varchar(120) not null default 'FACTORY_BOX_V1',
    user_id uuid null, printer_name text null, paper_size varchar(40) not null default 'A4',
    margin_top_mm numeric(8,2) not null default 0, margin_right_mm numeric(8,2) not null default 0,
    margin_bottom_mm numeric(8,2) not null default 0, margin_left_mm numeric(8,2) not null default 0,
    scale_percent numeric(8,2) not null default 100, horizontal_offset_mm numeric(8,2) not null default 0,
    vertical_offset_mm numeric(8,2) not null default 0, is_default boolean not null default false,
    created_at timestamptz not null default now(), updated_at timestamptz null,
    label_width_mm numeric(8,2) null default 95, label_height_mm numeric(8,2) null default 55,
    gap_x_mm numeric(8,2) not null default 4, gap_y_mm numeric(8,2) not null default 4,
    labels_per_page integer not null default 2, created_by uuid null, updated_by uuid null,
    reg_status char(1) not null default 'A'
);

-- Compatibilidade com a tabela criada pela fila de impressão.
alter table if exists ged.label_print_calibration add column if not exists user_id uuid null;
alter table if exists ged.label_print_calibration add column if not exists paper_size varchar(40) not null default 'A4';
alter table if exists ged.label_print_calibration add column if not exists margin_right_mm numeric(8,2) not null default 0;
alter table if exists ged.label_print_calibration add column if not exists margin_bottom_mm numeric(8,2) not null default 0;
alter table if exists ged.label_print_calibration add column if not exists horizontal_offset_mm numeric(8,2) not null default 0;
alter table if exists ged.label_print_calibration add column if not exists vertical_offset_mm numeric(8,2) not null default 0;
alter table if exists ged.label_print_calibration add column if not exists is_default boolean not null default false;

create index if not exists ix_label_print_calibration_tenant_user
on ged.label_print_calibration(tenant_id, user_id, is_default) where reg_status = 'A';
create unique index if not exists ux_label_print_calibration_tenant_template_printer
on ged.label_print_calibration(tenant_id, template_code, coalesce(printer_name, 'DEFAULT')) where reg_status='A';
