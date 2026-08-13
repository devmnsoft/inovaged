begin;
create schema if not exists ged;
create table if not exists ged.hospital_billing_document(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, document_version_id uuid null,
 document_type text not null default 'CONTA_HOSPITALAR', insurer text null, provider_name text null, provider_cnpj text null, cnes text null,
 guide_number text null, authorization_number text null, batch_number text null, invoice_number text null, competence text null,
 attendance_start date null, attendance_end date null, issue_date date null, due_date date null,
 patient_name text null, patient_document_encrypted text null, procedure_name text null, procedure_code text null, diagnosis_code text null,
 quantity numeric(14,4) null, unit_amount numeric(18,2) null, presented_amount numeric(18,2) not null default 0,
 approved_amount numeric(18,2) not null default 0, denied_amount numeric(18,2) not null default 0, recovered_amount numeric(18,2) not null default 0,
 tax_amount numeric(18,2) not null default 0, denial_reason text null, denial_status text null, appeal_filed boolean not null default false,
 contract_number text null, cost_center text null, confidence numeric(5,2) not null default 0, divergence_alerts jsonb not null default '[]',
 review_status text not null default 'PENDING_REVIEW', has_ocr boolean not null default false, reviewed_by uuid null, reviewed_at timestamptz null,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create unique index if not exists ux_hospital_billing_tenant_document on ged.hospital_billing_document(tenant_id,document_id) where reg_status='A';
create index if not exists ix_hospital_billing_work_queue on ged.hospital_billing_document(tenant_id,review_status,competence,created_at desc) where reg_status='A';
create index if not exists ix_hospital_billing_denials on ged.hospital_billing_document(tenant_id,denial_status,denied_amount desc) where reg_status='A' and denied_amount>0;

create table if not exists ged.smart_search_conversation(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, user_id uuid not null, title text null, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), reg_status char(1) not null default 'A');
create table if not exists ged.smart_search_message(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, conversation_id uuid not null, role text not null, content text not null,
 intent_json jsonb null, sources_json jsonb not null default '[]', created_at timestamptz not null default now(), reg_status char(1) not null default 'A');
create index if not exists ix_smart_search_conversation_user on ged.smart_search_conversation(tenant_id,user_id,updated_at desc) where reg_status='A';
create index if not exists ix_smart_search_message_conversation on ged.smart_search_message(tenant_id,conversation_id,created_at) where reg_status='A';
commit;
