create schema if not exists ged;
create table if not exists ged.billing_document_extraction (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, document_version_id uuid null,
 extraction_status text not null default 'PENDING', document_kind text null, supplier_name text null, supplier_document text null,
 invoice_number text null, invoice_series text null, issue_date date null, due_date date null, competence_month text null,
 gross_amount numeric(14,2) null, net_amount numeric(14,2) null, tax_amount numeric(14,2) null, iss_amount numeric(14,2) null,
 inss_amount numeric(14,2) null, pis_amount numeric(14,2) null, cofins_amount numeric(14,2) null, ir_amount numeric(14,2) null,
 csll_amount numeric(14,2) null, contract_number text null, purchase_order text null, cost_center text null, service_description text null,
 ust_quantity numeric(14,4) null, ust_unit_value numeric(14,4) null, confidence numeric(5,2) not null default 0,
 extracted_json jsonb not null default '{}'::jsonb, warnings_json jsonb not null default '[]'::jsonb, reviewed_by uuid null,
 reviewed_at timestamptz null, created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A'
);
create index if not exists ix_billing_extraction_tenant_status on ged.billing_document_extraction(tenant_id, extraction_status, created_at desc);
create index if not exists ix_billing_extraction_tenant_document on ged.billing_document_extraction(tenant_id, document_id);
create unique index if not exists ux_billing_extraction_tenant_document_active on ged.billing_document_extraction(tenant_id, document_id) where reg_status='A';
