-- Physical Archive 2.0. Additive and safe for installations that already use ged.box.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.physical_location (id uuid primary key default gen_random_uuid(), tenant_id uuid not null);
alter table ged.physical_location add column if not exists parent_id uuid null;
alter table ged.physical_location add column if not exists location_code text;
alter table ged.physical_location add column if not exists name text;
alter table ged.physical_location add column if not exists location_type varchar(60) not null default 'AREA';
alter table ged.physical_location add column if not exists description text;
alter table ged.physical_location add column if not exists capacity_boxes integer;
alter table ged.physical_location add column if not exists is_active boolean not null default true;
alter table ged.physical_location add column if not exists created_at timestamptz not null default now();
alter table ged.physical_location add column if not exists updated_at timestamptz;
alter table ged.physical_location add column if not exists reg_status char(1) not null default 'A';
update ged.physical_location set location_code=coalesce(location_code,id::text),name=coalesce(name,location_code,'Localização') where location_code is null or name is null;
alter table ged.physical_location alter column location_code set not null;
alter table ged.physical_location alter column name set not null;
do $$ begin if not exists(select 1 from pg_constraint where conname='fk_physical_location_parent') then alter table ged.physical_location add constraint fk_physical_location_parent foreign key(parent_id) references ged.physical_location(id); end if; end $$;

create table if not exists ged.physical_box (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, box_code text not null, label_code text null,
 title text null, description text null, location_id uuid null references ged.physical_location(id), status varchar(40) not null default 'ACTIVE',
 classification_summary text null, period_start date null, period_end date null, retention_status text null, current_holder text null,
 created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A');
create table if not exists ged.physical_box_document (
 id uuid primary key default gen_random_uuid(),tenant_id uuid not null,box_id uuid not null references ged.physical_box(id),document_id uuid not null,
 document_order integer null,notes text null,linked_by uuid null,linked_at timestamptz not null default now(),reg_status char(1) not null default 'A');
create table if not exists ged.physical_movement (
 id uuid primary key default gen_random_uuid(),tenant_id uuid not null,box_id uuid not null references ged.physical_box(id),from_location_id uuid null references ged.physical_location(id),to_location_id uuid null references ged.physical_location(id),movement_type varchar(60) not null default 'TRANSFER',reason text null,performed_by uuid null,performed_by_name text null,performed_at timestamptz not null default now(),payload_json jsonb null,reg_status char(1) not null default 'A');
create table if not exists ged.physical_inventory_session (
 id uuid primary key default gen_random_uuid(),tenant_id uuid not null,session_number text not null,title text not null,location_id uuid null references ged.physical_location(id),status varchar(40) not null default 'OPEN',started_by uuid null,started_at timestamptz not null default now(),closed_by uuid null,closed_at timestamptz null,notes text null,reg_status char(1) not null default 'A');
create table if not exists ged.physical_inventory_item (
 id uuid primary key default gen_random_uuid(),tenant_id uuid not null,session_id uuid not null references ged.physical_inventory_session(id),box_id uuid null references ged.physical_box(id),scanned_code text null,expected_location_id uuid null,found_location_id uuid null,result varchar(40) not null default 'PENDING',notes text null,scanned_by uuid null,scanned_at timestamptz null,reg_status char(1) not null default 'A');
create table if not exists ged.physical_loan (
 id uuid primary key default gen_random_uuid(),tenant_id uuid not null,loan_number text not null,box_id uuid null references ged.physical_box(id),document_id uuid null,requested_by_name text not null,requested_by_department text null,reason text null,status varchar(40) not null default 'OPEN',loaned_by uuid null,loaned_at timestamptz not null default now(),due_at timestamptz null,returned_by uuid null,returned_at timestamptz null,return_notes text null,reg_status char(1) not null default 'A');
create table if not exists ged.physical_custody_event (
 id uuid primary key default gen_random_uuid(),tenant_id uuid not null,source_type varchar(60) not null,source_id uuid not null,event_type varchar(80) not null,title text not null,description text null,performed_by uuid null,performed_by_name text null,occurred_at timestamptz not null default now(),correlation_id text null,payload_json jsonb null,reg_status char(1) not null default 'A');
create unique index if not exists ux_physical_location_code on ged.physical_location(tenant_id,location_code) where reg_status='A';
create unique index if not exists ux_physical_box_code on ged.physical_box(tenant_id,box_code) where reg_status='A';
create index if not exists ix_physical_box_location on ged.physical_box(tenant_id,location_id,status) where reg_status='A';
create unique index if not exists ux_physical_box_document_unique on ged.physical_box_document(tenant_id,box_id,document_id) where reg_status='A';
create index if not exists ix_physical_movement_box on ged.physical_movement(tenant_id,box_id,performed_at desc) where reg_status='A';
create index if not exists ix_physical_inventory_session_status on ged.physical_inventory_session(tenant_id,status,started_at desc) where reg_status='A';
create index if not exists ix_physical_loan_status on ged.physical_loan(tenant_id,status,due_at) where reg_status='A';
create index if not exists ix_physical_custody_event_source on ged.physical_custody_event(tenant_id,source_type,source_id,occurred_at desc) where reg_status='A';
