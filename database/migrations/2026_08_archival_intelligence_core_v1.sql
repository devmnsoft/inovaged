-- Núcleo Arquivístico Inteligente v1: fundações aditivas e tenant-aware.
create schema if not exists ged;

alter table if exists ged.classification_plan add column if not exists activity_type text not null default 'MEIO';
alter table if exists ged.classification_plan add column if not exists sort_order integer not null default 0;
alter table if exists ged.classification_plan add column if not exists current_term_text text null;
alter table if exists ged.classification_plan add column if not exists current_event text null;
alter table if exists ged.classification_plan add column if not exists intermediate_term_text text null;
alter table if exists ged.classification_plan add column if not exists intermediate_event text null;
alter table if exists ged.classification_plan add column if not exists normative_source text null;
alter table if exists ged.classification_plan add column if not exists condition_or_exception text null;
alter table if exists ged.classification_plan add column if not exists review_status text not null default 'PENDENTE';

create table if not exists ged.instrument_import (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, instrument_type text not null,
 source_format text not null, original_name text not null, content_hash text not null, status text not null default 'RECEBIDO',
 column_mapping jsonb null, requested_by uuid not null, created_at timestamptz not null default now(), published_at timestamptz null);
create table if not exists ged.instrument_import_item (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, import_id uuid not null references ged.instrument_import(id),
 row_number integer not null, raw_data jsonb not null, mapped_data jsonb null, validation_status text not null default 'PENDENTE', validation_errors jsonb null);
create table if not exists ged.classification_equivalence (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, source_version_id uuid not null, source_code text not null,
 target_version_id uuid not null, target_code text not null, equivalence_type text not null, approved_by uuid null, approved_at timestamptz null);

create table if not exists ged.document_extraction_run (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, document_version_id uuid not null,
 extractor_version text not null, status text not null default 'PROCESSANDO', started_at timestamptz not null default now(), completed_at timestamptz null, error_code text null);
create table if not exists ged.document_extracted_field (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, extraction_run_id uuid not null references ged.document_extraction_run(id),
 document_id uuid not null, document_version_id uuid not null, field_name text not null, raw_value text null, normalized_value text null,
 page_number integer null, evidence text null, extraction_method text not null, review_status text not null default 'SUGERIDO',
 sensitivity text not null default 'INTERNO', reviewed_by uuid null, reviewed_at timestamptz null, created_at timestamptz not null default now());
create table if not exists ged.document_extraction_review (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, extracted_field_id uuid not null references ged.document_extracted_field(id),
 previous_status text not null, new_status text not null, previous_value text null, new_value text null, reason text null,
 reviewed_by uuid not null, reviewed_at timestamptz not null default now());

create table if not exists ged.label_print_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, label_subject_type text not null, label_subject_id uuid not null,
 template_code text not null, snapshot_json jsonb not null, snapshot_sha256 text not null, printed_by uuid not null,
 printed_at timestamptz not null default now(), ip_address inet null, user_agent text null, reprint_reason text null);
create table if not exists ged.archival_object_acl (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, object_type text not null, object_id uuid not null,
 principal_type text not null, principal_id uuid not null, permission text not null, granted_by uuid not null, granted_at timestamptz not null default now(), revoked_at timestamptz null);

create index if not exists ix_instrument_import_review on ged.instrument_import(tenant_id,status,created_at desc);
create index if not exists ix_instrument_import_item_validation on ged.instrument_import_item(tenant_id,import_id,validation_status);
create index if not exists ix_classification_equivalence_lookup on ged.classification_equivalence(tenant_id,source_version_id,source_code);
create index if not exists ix_extraction_run_document on ged.document_extraction_run(tenant_id,document_id,started_at desc);
create index if not exists ix_extracted_field_review on ged.document_extracted_field(tenant_id,review_status,field_name);
create index if not exists ix_extracted_field_search on ged.document_extracted_field(tenant_id,field_name,normalized_value) where review_status <> 'REJEITADO';
create index if not exists ix_label_print_subject on ged.label_print_history(tenant_id,label_subject_type,label_subject_id,printed_at desc);
create index if not exists ix_archival_acl_lookup on ged.archival_object_acl(tenant_id,object_type,object_id,principal_id) where revoked_at is null;
