create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.label_evidence (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 subject_type varchar(40) not null, subject_id uuid null, control_number text null,
 inventory_session_id uuid null, scan_event_id uuid null,
 evidence_type varchar(40) not null default 'PHOTO', file_name text null,
 content_type varchar(120) null, file_size bigint null, storage_path text null,
 description text null, location_expected text null, location_found text null,
 captured_by uuid null, captured_at timestamptz not null default now(),
 latitude numeric(12,8) null, longitude numeric(12,8) null, ip_address inet null,
 user_agent text null, payload_json jsonb null, reg_status char(1) not null default 'A'
);
create table if not exists ged.label_mobile_sync_log (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 client_sync_id varchar(120) not null, device_id varchar(120) null, user_id uuid null,
 sync_status varchar(40) not null default 'RECEIVED', received_items integer not null default 0,
 accepted_items integer not null default 0, rejected_items integer not null default 0,
 error_message text null, payload_json jsonb null, synced_at timestamptz not null default now(),
 ip_address inet null, user_agent text null, reg_status char(1) not null default 'A'
);
alter table ged.label_scan_event add column if not exists mobile_client_id varchar(120) null;
alter table ged.label_scan_event add column if not exists captured_at timestamptz null;
alter table ged.label_scan_event add column if not exists device_id varchar(120) null;
create index if not exists ix_label_evidence_subject on ged.label_evidence(tenant_id,subject_type,subject_id,captured_at desc);
create index if not exists ix_label_evidence_inventory on ged.label_evidence(tenant_id,inventory_session_id,captured_at desc);
create unique index if not exists ux_label_mobile_sync_tenant_client on ged.label_mobile_sync_log(tenant_id,client_sync_id);
create index if not exists ix_label_mobile_sync_user on ged.label_mobile_sync_log(tenant_id,user_id,synced_at desc);
create unique index if not exists ux_label_scan_mobile_client on ged.label_scan_event(tenant_id,mobile_client_id) where mobile_client_id is not null;
