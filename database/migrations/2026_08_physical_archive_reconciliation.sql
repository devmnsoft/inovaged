-- Saneamento inteligente do acervo. Migration aditiva e idempotente.
begin;
create schema if not exists ged;
alter table if exists ged.label_print_history add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.archive_reconciliation_run (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, run_number varchar(40) not null,
 source varchar(40) not null default 'MANUAL', inventory_session_id uuid, status varchar(40) not null default 'RUNNING',
 total_checked integer not null default 0, total_issues integer not null default 0,
 total_critical integer not null default 0, total_high integer not null default 0,
 total_medium integer not null default 0, total_low integer not null default 0,
 started_by uuid, started_at timestamptz not null default now(), finished_at timestamptz,
 error_message text, payload_json jsonb, reg_status char(1) not null default 'A'
);
create table if not exists ged.archive_reconciliation_issue (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 run_id uuid not null references ged.archive_reconciliation_run(id), issue_type varchar(80) not null,
 severity varchar(30) not null default 'MEDIUM', subject_type varchar(40), subject_id uuid,
 control_number text, box_id uuid, document_id uuid, expected_value text, found_value text,
 title text not null, description text not null, suggestion text, proposed_action varchar(80),
 proposed_payload jsonb, status varchar(40) not null default 'OPEN', resolved_by uuid,
 resolved_at timestamptz, resolution_notes text, created_at timestamptz not null default now(),
 reg_status char(1) not null default 'A'
);
create table if not exists ged.archive_reconciliation_fix (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 issue_id uuid not null references ged.archive_reconciliation_issue(id), action_type varchar(80) not null,
 before_json jsonb, after_json jsonb, applied_by uuid, applied_at timestamptz not null default now(),
 notes text, reg_status char(1) not null default 'A'
);
create unique index if not exists ux_archive_reconciliation_run_tenant_number on ged.archive_reconciliation_run(tenant_id,run_number);
create index if not exists ix_archive_reconciliation_issue_tenant_status on ged.archive_reconciliation_issue(tenant_id,status,severity,created_at desc);
create index if not exists ix_archive_reconciliation_issue_subject on ged.archive_reconciliation_issue(tenant_id,subject_type,subject_id);
create index if not exists ix_archive_reconciliation_issue_box on ged.archive_reconciliation_issue(tenant_id,box_id);
create index if not exists ix_archive_reconciliation_issue_document on ged.archive_reconciliation_issue(tenant_id,document_id);
commit;
