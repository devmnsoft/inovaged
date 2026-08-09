-- Consolidação arquivística premium. Aditiva, idempotente e sem perda de dados.
begin;
create schema if not exists ged;

alter table if exists ged.classification_plan
  add column if not exists activity_type varchar(10) not null default 'MEIO',
  add column if not exists current_retention_text text,
  add column if not exists current_start_event text,
  add column if not exists intermediate_retention_text text,
  add column if not exists intermediate_start_event text,
  add column if not exists final_destination text,
  add column if not exists normative_source text,
  add column if not exists condition_exception text,
  add column if not exists notes text,
  add column if not exists confidentiality_level varchar(30) not null default 'PUBLICO',
  add column if not exists requires_digital_signature boolean not null default false,
  add column if not exists review_status varchar(30) not null default 'RASCUNHO';

alter table if exists ged.label_print
  add column if not exists tenant_id uuid,
  add column if not exists user_id uuid,
  add column if not exists snapshot_json jsonb,
  add column if not exists snapshot_sha256 char(64),
  add column if not exists template_code varchar(60),
  add column if not exists template_version varchar(20),
  add column if not exists reprint_reason text,
  add column if not exists ip_address inet,
  add column if not exists user_agent text,
  add column if not exists printed_at timestamptz not null default now();

create table if not exists ged.label_print_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 label_subject_type varchar(20) not null, label_subject_id uuid not null,
 template_code varchar(60) not null, snapshot_json jsonb not null,
 snapshot_sha256 char(64) not null, printed_by uuid not null,
 printed_at timestamptz not null default now(), ip_address inet,
 user_agent text, reprint_reason text
);

create table if not exists ged.box_location_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 box_id uuid not null, previous_location_id uuid, new_location_id uuid,
 reason text not null, moved_by uuid not null, moved_at timestamptz not null default now(),
 inventory_session_id uuid, reg_status char(1) not null default 'A'
);

create table if not exists ged.physical_inventory_session (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 location_id uuid, status varchar(20) not null default 'OPEN', opened_by uuid not null,
 opened_at timestamptz not null default now(), closed_by uuid, closed_at timestamptz, notes text
);
create table if not exists ged.physical_inventory_item (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 session_id uuid not null references ged.physical_inventory_session(id), box_id uuid,
 expected_location_id uuid, observed_location_id uuid, result varchar(30) not null,
 notes text, checked_by uuid not null, checked_at timestamptz not null default now()
);

create index if not exists ix_label_print_history_subject
 on ged.label_print_history(tenant_id,label_subject_type,label_subject_id,printed_at desc);
create index if not exists ix_box_location_history_box
 on ged.box_location_history(tenant_id,box_id,moved_at desc);
create index if not exists ix_inventory_session_location
 on ged.physical_inventory_session(tenant_id,location_id,status,opened_at desc);
create index if not exists ix_inventory_item_session
 on ged.physical_inventory_item(tenant_id,session_id,result);
create index if not exists ix_classification_plan_tree
 on ged.classification_plan(tenant_id,parent_id,code) where is_active;

create or replace view ged.vw_physical_map as
select b.tenant_id, b.id as box_id, b.box_no, b.label_code, b.location_id,
       concat_ws(' / ',p.unit_name,p.building,p.room,p.aisle,p.rack,p.shelf,p.pallet,p.location_code) as full_location,
       count(distinct bi.document_id) filter (where bi.reg_status='A') as document_count,
       count(distinct dc.classification_plan_id) filter (where bi.reg_status='A') as classification_count,
       count(distinct cp.current_retention_text) filter (where bi.reg_status='A') as retention_count,
       count(distinct cp.confidentiality_level) filter (where bi.reg_status='A') as confidentiality_count,
       count(distinct cp.final_destination) filter (where bi.reg_status='A') as destination_count
from ged.box b
left join ged.physical_location p on p.tenant_id=b.tenant_id and p.id=b.location_id
left join ged.batch_item bi on bi.tenant_id=b.tenant_id and bi.box_id=b.id
left join ged.document_classification dc on dc.tenant_id=bi.tenant_id and dc.document_id=bi.document_id and dc.reg_status='A'
left join ged.classification_plan cp on cp.tenant_id=dc.tenant_id and cp.id=dc.classification_plan_id
where b.reg_status='A'
group by b.tenant_id,b.id,b.box_no,b.label_code,b.location_id,p.unit_name,p.building,p.room,p.aisle,p.rack,p.shelf,p.pallet,p.location_code;
commit;
