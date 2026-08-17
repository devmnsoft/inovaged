-- Central executiva: rastreabilidade de exportações e índices tenant-first.
-- Aditiva e idempotente; não depende de unaccent ou pg_trgm.
begin;
create schema if not exists ged;

create table if not exists ged.report_export_audit (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    user_id uuid null,
    report_code varchar(80) not null,
    export_format varchar(12) not null,
    filters_json jsonb not null default '{}',
    row_count integer not null default 0,
    contains_sensitive_data boolean not null default false,
    created_at timestamptz not null default now(),
    correlation_id varchar(100) null
);

create index if not exists ix_report_export_audit_tenant_created
    on ged.report_export_audit(tenant_id, created_at desc);
create index if not exists ix_document_tenant_created_active
    on ged.document(tenant_id, created_at desc, id) where reg_status='A';
create index if not exists ix_document_search_tenant_document_ocr
    on ged.document_search(tenant_id, document_id) where nullif(trim(ocr_text),'') is not null;
create index if not exists ix_document_classification_tenant_document_active
    on ged.document_classification(tenant_id, document_id) where reg_status='A';
create index if not exists ix_smart_search_query_audit_no_result
    on ged.smart_search_query_audit(tenant_id, created_at desc) where result_count=0;
create index if not exists ix_hospital_billing_tenant_insurer_values
    on ged.hospital_billing_document(tenant_id, insurer) include (presented_amount, approved_amount, denied_amount, recovered_amount)
    where reg_status='A';
commit;
