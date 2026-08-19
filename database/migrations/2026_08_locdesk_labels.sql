create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.locdesk_label_draft (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, label_kind varchar(20) not null,
 archive_title text not null, process_number text null, control_number varchar(50) not null,
 volume_number integer not null default 1, volume_total integer not null default 1, subject text null,
 details text null, activity text null, classification text null, support text null, document_period text null,
 current_phase text null, elimination_forecast text null, elimination_status text null, led_number text null,
 location text null, source_box_id uuid null, source_document_id uuid null, qr_payload text null,
 created_by uuid null, created_at timestamptz not null default now(), updated_by uuid null,
 updated_at timestamptz null, reg_status char(1) not null default 'A',
 constraint ck_locdesk_label_kind check (label_kind in ('FOLDER','BOX')),
 constraint ck_locdesk_label_volume check (volume_number >= 1 and volume_total >= volume_number)
);
alter table ged.locdesk_label_draft add column if not exists qr_payload text;
create index if not exists ix_locdesk_label_draft_tenant_kind on ged.locdesk_label_draft(tenant_id,label_kind,reg_status);
create index if not exists ix_locdesk_label_draft_control on ged.locdesk_label_draft(tenant_id,control_number);
create index if not exists ix_locdesk_label_draft_box on ged.locdesk_label_draft(tenant_id,source_box_id) where source_box_id is not null;
create index if not exists ix_locdesk_label_draft_document on ged.locdesk_label_draft(tenant_id,source_document_id) where source_document_id is not null;
