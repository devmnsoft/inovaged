-- InovaGED - consolidação definitiva do schema arquivístico.
-- Migration aditiva e idempotente: não remove nem sobrescreve dados existentes.
begin;
create schema if not exists ged;
do $$ begin
 create extension if not exists pgcrypto;
exception when insufficient_privilege then
 raise notice 'Sem permissão para criar pgcrypto; gen_random_uuid deve existir no ambiente.';
end $$;

create table if not exists ged.schema_migration_history (
 id uuid primary key default gen_random_uuid(), script_name text not null,
 applied_at timestamptz not null default now(), applied_by text,
 checksum_sha256 text, success boolean not null default true, notes text
);
create unique index if not exists ux_schema_migration_history_script on ged.schema_migration_history(script_name);

create table if not exists ged.document_folder_move_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null,
 old_folder_id uuid, new_folder_id uuid, moved_by uuid, moved_by_name text,
 moved_at timestamptz not null default now(), reason text, batch_id uuid,
 source varchar(32) not null default 'SINGLE', reg_status char(1) not null default 'A'
);
alter table ged.document_folder_move_history add column if not exists id uuid default gen_random_uuid();
alter table ged.document_folder_move_history add column if not exists tenant_id uuid;
alter table ged.document_folder_move_history add column if not exists document_id uuid;
alter table ged.document_folder_move_history add column if not exists old_folder_id uuid;
alter table ged.document_folder_move_history add column if not exists new_folder_id uuid;
alter table ged.document_folder_move_history add column if not exists moved_by uuid;
alter table ged.document_folder_move_history add column if not exists moved_by_name text;
alter table ged.document_folder_move_history add column if not exists moved_at timestamptz default now();
alter table ged.document_folder_move_history add column if not exists reason text;
alter table ged.document_folder_move_history add column if not exists batch_id uuid;
alter table ged.document_folder_move_history add column if not exists source varchar(32) default 'SINGLE';
alter table ged.document_folder_move_history add column if not exists reg_status char(1) default 'A';

create table if not exists ged.classification_plan (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, parent_id uuid, code text not null,
 title text not null default '', description text, activity_type varchar(10) not null default 'MEIO',
 display_order integer not null default 0, current_retention_text text, current_start_event text,
 intermediate_retention_text text, intermediate_start_event text, final_destination text,
 normative_source text, condition_exception text, notes text,
 confidentiality_level varchar(30) not null default 'PUBLICO', requires_digital_signature boolean not null default false,
 review_status varchar(30) not null default 'RASCUNHO', is_active boolean not null default true,
 created_at timestamptz not null default now(), created_by uuid, updated_at timestamptz, updated_by uuid,
 reg_status char(1) not null default 'A'
);
alter table ged.classification_plan add column if not exists tenant_id uuid;
alter table ged.classification_plan add column if not exists parent_id uuid;
alter table ged.classification_plan add column if not exists code text;
alter table ged.classification_plan add column if not exists title text;
alter table ged.classification_plan add column if not exists description text;
alter table ged.classification_plan add column if not exists activity_type varchar(10) default 'MEIO';
alter table ged.classification_plan add column if not exists display_order integer default 0;
alter table ged.classification_plan add column if not exists current_retention_text text;
alter table ged.classification_plan add column if not exists current_start_event text;
alter table ged.classification_plan add column if not exists intermediate_retention_text text;
alter table ged.classification_plan add column if not exists intermediate_start_event text;
alter table ged.classification_plan add column if not exists final_destination text;
alter table ged.classification_plan add column if not exists normative_source text;
alter table ged.classification_plan add column if not exists condition_exception text;
alter table ged.classification_plan add column if not exists notes text;
alter table ged.classification_plan add column if not exists confidentiality_level varchar(30) default 'PUBLICO';
alter table ged.classification_plan add column if not exists requires_digital_signature boolean default false;
alter table ged.classification_plan add column if not exists review_status varchar(30) default 'RASCUNHO';
alter table ged.classification_plan add column if not exists is_active boolean default true;
alter table ged.classification_plan add column if not exists created_at timestamptz default now();
alter table ged.classification_plan add column if not exists created_by uuid;
alter table ged.classification_plan add column if not exists updated_at timestamptz;
alter table ged.classification_plan add column if not exists updated_by uuid;
alter table ged.classification_plan add column if not exists reg_status char(1) default 'A';

create table if not exists ged.classification_plan_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, classification_plan_id uuid not null,
 action text not null, old_data jsonb, new_data jsonb, reason text, changed_by uuid,
 created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create table if not exists ged.classification_plan_version (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, version_no integer,
 title text not null default '', notes text, published_by uuid, published_at timestamptz,
 created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create table if not exists ged.classification_plan_version_item (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, version_id uuid not null,
 classification_plan_id uuid, parent_id uuid, code text, title text, description text,
 activity_type varchar(10), display_order integer, current_retention_text text, current_start_event text,
 intermediate_retention_text text, intermediate_start_event text, final_destination text,
 normative_source text, condition_exception text, confidentiality_level varchar(30), review_status varchar(30),
 snapshot_json jsonb, reg_status char(1) not null default 'A'
);
create table if not exists ged.document_classification (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null,
 classification_plan_id uuid, classification_version_id uuid, confidence numeric(5,4),
 suggestion_factors jsonb not null default '{}'::jsonb, reclassification_reason text,
 source text, classified_by uuid, classified_at timestamptz not null default now(),
 reg_status char(1) not null default 'A'
);
create table if not exists ged.document_classification_audit (
 id bigserial primary key, tenant_id uuid not null, document_id uuid not null,
 previous_classification_id uuid, new_classification_id uuid, previous_version_id uuid, new_version_id uuid,
 reason text, impact_json jsonb not null default '{}'::jsonb, changed_by uuid,
 changed_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
alter table ged.document_classification_audit add column if not exists created_at timestamptz default now();

create table if not exists ged.label_print (
 id uuid primary key default gen_random_uuid(), tenant_id uuid, box_id uuid, document_id uuid,
 label_type varchar(30), printed_by uuid, printed_at timestamptz not null default now(),
 ip_address inet, user_agent text, data jsonb, snapshot_json jsonb, payload_hash_sha256 char(64),
 template_version varchar(60), reprint_reason text, print_channel varchar(30) default 'WEB',
 reg_status char(1) not null default 'A'
);
alter table ged.label_print add column if not exists tenant_id uuid;
alter table ged.label_print add column if not exists box_id uuid;
alter table ged.label_print add column if not exists document_id uuid;
alter table ged.label_print add column if not exists label_type varchar(30);
alter table ged.label_print add column if not exists printed_by uuid;
alter table ged.label_print add column if not exists printed_at timestamptz default now();
alter table ged.label_print add column if not exists ip_address inet;
alter table ged.label_print add column if not exists user_agent text;
alter table ged.label_print add column if not exists data jsonb;
alter table ged.label_print add column if not exists snapshot_json jsonb;
alter table ged.label_print add column if not exists payload_hash_sha256 char(64);
alter table ged.label_print add column if not exists template_version varchar(60);
alter table ged.label_print add column if not exists reprint_reason text;
alter table ged.label_print add column if not exists print_channel varchar(30) default 'WEB';
alter table ged.label_print add column if not exists reg_status char(1) default 'A';
create table if not exists ged.label_print_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, label_subject_type varchar(20) not null,
 label_subject_id uuid not null, template_code varchar(60) not null, snapshot_json jsonb not null,
 snapshot_sha256 char(64) not null, printed_by uuid not null, printed_at timestamptz not null default now(),
 ip_address inet, user_agent text, reprint_reason text, reg_status char(1) not null default 'A'
);

create table if not exists ged.physical_location (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, location_code text,
 unit_name text, property_name text, address_street text, address_number text, address_district text,
 address_city text, address_state text, address_zip text, building text, room text, aisle text,
 rack text, shelf text, pallet text, notes text, reg_date timestamptz default now(),
 created_at timestamptz default now(), updated_at timestamptz, reg_status char(1) not null default 'A'
);
alter table ged.physical_location add column if not exists tenant_id uuid;
alter table ged.physical_location add column if not exists location_code text;
alter table ged.physical_location add column if not exists unit_name text;
alter table ged.physical_location add column if not exists property_name text;
alter table ged.physical_location add column if not exists address_street text;
alter table ged.physical_location add column if not exists address_number text;
alter table ged.physical_location add column if not exists address_district text;
alter table ged.physical_location add column if not exists address_city text;
alter table ged.physical_location add column if not exists address_state text;
alter table ged.physical_location add column if not exists address_zip text;
alter table ged.physical_location add column if not exists building text;
alter table ged.physical_location add column if not exists room text;
alter table ged.physical_location add column if not exists aisle text;
alter table ged.physical_location add column if not exists rack text;
alter table ged.physical_location add column if not exists shelf text;
alter table ged.physical_location add column if not exists pallet text;
alter table ged.physical_location add column if not exists notes text;
alter table ged.physical_location add column if not exists reg_date timestamptz default now();
alter table ged.physical_location add column if not exists created_at timestamptz default now();
alter table ged.physical_location add column if not exists updated_at timestamptz;
alter table ged.physical_location add column if not exists updated_by uuid;
alter table ged.physical_location add column if not exists full_location_code text;
alter table ged.physical_location add column if not exists reg_status char(1) default 'A';

create table if not exists ged.box (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, box_no text not null,
 location_id uuid, label_code text, notes text, status varchar(30) not null default 'OPEN',
 closed_at timestamptz, closed_by uuid, created_at timestamptz default now(), updated_at timestamptz,
 reg_date timestamptz default now(), reg_status char(1) not null default 'A'
);
alter table ged.box add column if not exists tenant_id uuid;
alter table ged.box add column if not exists box_no text;
alter table ged.box add column if not exists location_id uuid;
alter table ged.box add column if not exists label_code text;
alter table ged.box add column if not exists notes text;
alter table ged.box add column if not exists status varchar(30) default 'OPEN';
alter table ged.box add column if not exists closed_at timestamptz;
alter table ged.box add column if not exists closed_by uuid;
alter table ged.box add column if not exists created_at timestamptz default now();
alter table ged.box add column if not exists updated_at timestamptz;
alter table ged.box add column if not exists reg_date timestamptz default now();
alter table ged.box add column if not exists reg_status char(1) default 'A';
alter table ged.box add column if not exists lifecycle_status varchar(20) default 'OPEN';
alter table ged.box add column if not exists is_full boolean default false;
alter table ged.box add column if not exists last_moved_at timestamptz;
alter table ged.box add column if not exists last_moved_by uuid;

create table if not exists ged.batch (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, batch_no text not null,
 status varchar(30) not null default 'RECEIVED', notes text, created_by uuid,
 created_at timestamptz default now(), updated_by uuid, updated_at timestamptz,
 reg_date timestamptz default now(), reg_status char(1) not null default 'A'
);
alter table ged.batch add column if not exists tenant_id uuid;
alter table ged.batch add column if not exists batch_no text;
alter table ged.batch add column if not exists status varchar(30) default 'RECEIVED';
alter table ged.batch add column if not exists notes text;
alter table ged.batch add column if not exists created_by uuid;
alter table ged.batch add column if not exists created_at timestamptz default now();
alter table ged.batch add column if not exists updated_by uuid;
alter table ged.batch add column if not exists updated_at timestamptz;
alter table ged.batch add column if not exists reg_date timestamptz default now();
alter table ged.batch add column if not exists reg_status char(1) default 'A';
create table if not exists ged.batch_item (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, batch_id uuid not null,
 document_id uuid not null, box_id uuid, reg_date timestamptz default now(), removed_at timestamptz,
 removed_by uuid, removed_reason text, reg_status char(1) not null default 'A'
);
alter table ged.batch_item add column if not exists tenant_id uuid;
alter table ged.batch_item add column if not exists batch_id uuid;
alter table ged.batch_item add column if not exists document_id uuid;
alter table ged.batch_item add column if not exists box_id uuid;
alter table ged.batch_item add column if not exists reg_date timestamptz default now();
alter table ged.batch_item add column if not exists removed_at timestamptz;
alter table ged.batch_item add column if not exists removed_by uuid;
alter table ged.batch_item add column if not exists removed_reason text;
alter table ged.batch_item add column if not exists reg_status char(1) default 'A';
create table if not exists ged.batch_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, batch_id uuid not null,
 from_status varchar(30), to_status varchar(30) not null, reason text, changed_by uuid,
 changed_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create table if not exists ged.box_content_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, box_id uuid not null,
 document_id uuid not null, action varchar(30) not null, reason text, performed_by uuid,
 changed_at timestamptz not null default now(), batch_id uuid, reg_status char(1) not null default 'A'
);
alter table ged.box_content_history add column if not exists changed_at timestamptz default now();
create table if not exists ged.box_location_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, box_id uuid not null,
 previous_location_id uuid, new_location_id uuid, reason text not null, moved_by uuid,
 moved_at timestamptz not null default now(), inventory_session_id uuid, reg_status char(1) not null default 'A'
);
alter table ged.box_location_history add column if not exists changed_at timestamptz default now();
create table if not exists ged.physical_location_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, location_id uuid not null,
 action varchar(30) not null, old_data jsonb, new_data jsonb, reason text, changed_by uuid,
 changed_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create table if not exists ged.loan_collection_event (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, loan_request_id uuid,
 event_type varchar(30) not null, notes text, occurred_at timestamptz not null default now(),
 occurred_by uuid, reg_status char(1) not null default 'A'
);

create index if not exists ix_dfmh_tenant_document_moved on ged.document_folder_move_history(tenant_id,document_id,moved_at desc);
create index if not exists ix_dfmh_batch on ged.document_folder_move_history(tenant_id,batch_id);
create index if not exists ix_label_print_tenant_printed on ged.label_print(tenant_id,printed_at desc);
create index if not exists ix_label_print_box on ged.label_print(tenant_id,box_id);
create index if not exists ix_label_print_document on ged.label_print(tenant_id,document_id);
create index if not exists ix_location_tenant_code on ged.physical_location(tenant_id,location_code);
create index if not exists ix_box_tenant_location_status on ged.box(tenant_id,location_id,status);
create index if not exists ix_batch_tenant_status on ged.batch(tenant_id,status);
create index if not exists ix_batch_item_batch on ged.batch_item(tenant_id,batch_id);
create index if not exists ix_batch_item_box on ged.batch_item(tenant_id,box_id);
create index if not exists ix_batch_item_document on ged.batch_item(tenant_id,document_id);
create unique index if not exists ux_batch_item_active_document on ged.batch_item(tenant_id,document_id) where reg_status='A' and removed_at is null;
create index if not exists ix_box_content_history_box on ged.box_content_history(tenant_id,box_id,changed_at desc);
create index if not exists ix_box_location_history_box on ged.box_location_history(tenant_id,box_id,changed_at desc);
create index if not exists ix_classification_plan_tenant on ged.classification_plan(tenant_id,parent_id);
create index if not exists ix_document_classification_document on ged.document_classification(tenant_id,document_id);

create or replace view ged.vw_physical_map as
select b.tenant_id, b.id box_id, b.box_no, b.label_code, b.location_id,
 concat_ws(' / ',p.unit_name,p.property_name,p.building,p.room,p.aisle,p.rack,p.shelf,p.pallet,p.location_code) full_location,
 count(d.id) over (partition by b.tenant_id,b.id) document_count,
 count(dc.id) over (partition by b.tenant_id,b.id) classification_count,
 count(cp.current_retention_text) over (partition by b.tenant_id,b.id) retention_count,
 count(cp.confidentiality_level) over (partition by b.tenant_id,b.id) confidentiality_count,
 count(cp.final_destination) over (partition by b.tenant_id,b.id) destination_count,
 d.id document_id, d.code document_code, d.title document_title,
 bt.id batch_id, bt.batch_no, bt.status batch_status,
 p.location_code, p.property_name, p.building, p.room, p.aisle, p.rack, p.shelf, p.pallet,
 bi.reg_date linked_at
from ged.batch_item bi
join ged.document d on d.tenant_id=bi.tenant_id and d.id=bi.document_id
left join ged.document_classification dc on dc.tenant_id=d.tenant_id and dc.document_id=d.id and dc.reg_status='A'
left join ged.classification_plan cp on cp.tenant_id=dc.tenant_id and cp.id=dc.classification_plan_id
left join ged.batch bt on bt.tenant_id=bi.tenant_id and bt.id=bi.batch_id
left join ged.box b on b.tenant_id=bi.tenant_id and b.id=bi.box_id and b.reg_status='A'
left join ged.physical_location p on p.tenant_id=b.tenant_id and p.id=b.location_id and p.reg_status='A'
where bi.reg_status='A' and bi.removed_at is null;

insert into ged.schema_migration_history(script_name, applied_by, checksum_sha256, success, notes)
values ('2026_08_archival_schema_consolidation', current_user, null, true,
 'Consolidação aditiva de classificação, etiquetas, lotes, acervo físico e movimentação documental.')
on conflict (script_name) do update set applied_at=now(), applied_by=current_user, success=true, notes=excluded.notes;
commit;
