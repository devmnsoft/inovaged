create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.label_alert (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, alert_type varchar(60) not null,
 severity varchar(30) not null default 'MEDIUM', subject_type varchar(40), subject_id uuid,
 control_number text, location text, title text not null, message text not null,
 status varchar(30) not null default 'OPEN', detected_at timestamptz not null default now(),
 resolved_by uuid, resolved_at timestamptz, resolution_notes text, reg_status char(1) not null default 'A'
);
create table if not exists ged.label_custody_event (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, subject_type varchar(40) not null,
 subject_id uuid, control_number text, event_type varchar(60) not null, event_title text not null,
 event_description text, source_table text, source_id uuid, location_from text, location_to text,
 performed_by uuid, performed_at timestamptz not null default now(), ip_address inet, user_agent text,
 payload_json jsonb, reg_status char(1) not null default 'A'
);
create table if not exists ged.label_operational_snapshot (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, snapshot_date date not null default current_date,
 total_labels_printed integer not null default 0, total_labels_scanned integer not null default 0,
 total_never_scanned integer not null default 0, total_location_divergence integer not null default 0,
 total_replacements integer not null default 0, total_boxes_without_label integer not null default 0,
 total_documents_without_label integer not null default 0, payload_json jsonb, created_at timestamptz not null default now(),
 reg_status char(1) not null default 'A'
);
create index if not exists ix_label_alert_tenant_status on ged.label_alert(tenant_id,status,detected_at desc);
create index if not exists ix_label_alert_subject on ged.label_alert(tenant_id,subject_type,subject_id);
create index if not exists ix_label_custody_subject on ged.label_custody_event(tenant_id,subject_type,subject_id,performed_at desc);
create index if not exists ix_label_custody_control on ged.label_custody_event(tenant_id,control_number,performed_at desc);
create unique index if not exists ux_label_operational_snapshot_tenant_date on ged.label_operational_snapshot(tenant_id,snapshot_date) where reg_status='A';
create unique index if not exists ux_label_alert_open_fingerprint on ged.label_alert(tenant_id,alert_type,coalesce(subject_id,'00000000-0000-0000-0000-000000000000'::uuid),coalesce(control_number,'')) where status in ('OPEN','IN_PROGRESS') and reg_status='A';
