begin;
create schema if not exists ged;
create extension if not exists pgcrypto;
create sequence if not exists ged.label_trace_code_seq;

create table if not exists ged.label_trace_identity (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, label_print_id uuid null,
 trace_token_hash text not null, trace_code varchar(80) not null, subject_type varchar(60) not null,
 subject_id uuid null, template_code text not null, template_version integer null,
 status varchar(40) not null default 'ACTIVE', issued_by uuid null, issued_by_name text null,
 issued_at timestamptz not null default now(), replaced_by_trace_id uuid null, replaced_at timestamptz null,
 replacement_reason text null, revoked_by uuid null, revoked_at timestamptz null, revoke_reason text null,
 payload_hash text null, reg_status char(1) not null default 'A'
);
alter table ged.label_trace_identity drop constraint if exists ck_label_trace_identity_status;
alter table ged.label_trace_identity add constraint ck_label_trace_identity_status check(status in ('ACTIVE','REPLACED','REVOKED','DAMAGED','LOST'));

create table if not exists ged.label_scan_event (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 trace_id uuid null references ged.label_trace_identity(id), scan_source varchar(60) not null default 'WEB',
 scanned_by uuid null, scanned_by_name text null, client_ip text null, user_agent text null,
 scan_result varchar(40) not null default 'VALID', location_hint text null, notes text null,
 scanned_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
-- Compatibility with installations that already have Tracking 1.0.
alter table ged.label_scan_event add column if not exists trace_id uuid null references ged.label_trace_identity(id);
alter table ged.label_scan_event add column if not exists scanned_by_name text null;
alter table ged.label_scan_event add column if not exists client_ip text null;
alter table ged.label_scan_event add column if not exists scan_result varchar(40) not null default 'VALID';
alter table ged.label_scan_event add column if not exists location_hint text null;
alter table ged.label_scan_event add column if not exists notes text null;

create table if not exists ged.label_replacement_event (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 old_trace_id uuid not null references ged.label_trace_identity(id), new_trace_id uuid null references ged.label_trace_identity(id),
 reason text not null, requested_by uuid null, requested_by_name text null, requested_at timestamptz not null default now(),
 status varchar(40) not null default 'COMPLETED', reg_status char(1) not null default 'A'
);
create table if not exists ged.label_qr_quality_issue (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, template_code text not null,
 trace_id uuid null references ged.label_trace_identity(id), issue_type varchar(80) not null,
 severity varchar(30) not null default 'MEDIUM', title text not null, description text null,
 recommended_action text null, status varchar(40) not null default 'OPEN', created_at timestamptz not null default now(),
 reg_status char(1) not null default 'A'
);
create unique index if not exists ux_label_trace_identity_code on ged.label_trace_identity(tenant_id,trace_code) where reg_status='A';
create index if not exists ix_label_trace_identity_hash on ged.label_trace_identity(trace_token_hash) where reg_status='A';
create index if not exists ix_label_trace_identity_subject on ged.label_trace_identity(tenant_id,subject_type,subject_id) where reg_status='A';
create index if not exists ix_label_scan_event_trace on ged.label_scan_event(trace_id,scanned_at desc) where reg_status='A';
create index if not exists ix_label_replacement_event_old on ged.label_replacement_event(old_trace_id,requested_at desc) where reg_status='A';
create index if not exists ix_label_qr_quality_issue_template on ged.label_qr_quality_issue(tenant_id,template_code,severity,status) where reg_status='A';
commit;
