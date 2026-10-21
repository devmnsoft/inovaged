-- Additive migration: preserve reviewed data, candidates, concurrency and financial records.
alter table ged.billing_document_extraction
 add column if not exists processing_status text not null default 'EXTRACTED',
 add column if not exists revision bigint not null default 0,
 add column if not exists pending_candidate_json jsonb null,
 add column if not exists responsible_user_id uuid null,
 add column if not exists review_due_date date null,
 add column if not exists priority integer not null default 0,
 add column if not exists divergence_reason text null,
 add column if not exists review_notes text null;

create table if not exists ged.billing_extraction_candidate (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, extraction_id uuid not null,
 document_version_id uuid null, snapshot_json jsonb not null, fingerprint text not null,
 created_at timestamptz not null default now(), unique(tenant_id, extraction_id, fingerprint)
);
alter table ged.billing_extraction_review_history
 add column if not exists previous_snapshot_json jsonb null,
 add column if not exists reason text null;

create or replace function ged.audit_billing_extraction_review() returns trigger language plpgsql as $$
begin
 if new.reviewed_at is distinct from old.reviewed_at and new.reviewed_at is not null then
  insert into ged.billing_extraction_review_history(tenant_id,extraction_id,old_status,new_status,reviewed_by,snapshot_json,previous_snapshot_json,reason)
  values(new.tenant_id,new.id,old.extraction_status,new.extraction_status,new.reviewed_by,
   to_jsonb(new)-'extracted_json'-'pending_candidate_json',to_jsonb(old)-'extracted_json'-'pending_candidate_json',new.review_notes);
 end if;
 return new;
end $$;

create index if not exists ix_billing_queue on ged.billing_document_extraction(tenant_id,priority desc,review_due_date,id) where reg_status='A';
create index if not exists ix_billing_duplicate on ged.billing_document_extraction(tenant_id,invoice_number,invoice_series,competence_month) where reg_status='A';

create table if not exists ged.billing_duplicate_policy (
 tenant_id uuid primary key, compare_series boolean not null default true,
 compare_competence boolean not null default true, compare_amount boolean not null default false,
 updated_by uuid not null, updated_at timestamptz not null default now()
);
create table if not exists ged.billing_duplicate_policy_history (
 id uuid primary key default gen_random_uuid(),tenant_id uuid not null,updated_by uuid not null,
 changed_at timestamptz not null default now(),previous_snapshot jsonb null,new_snapshot jsonb not null
);
create or replace function ged.audit_billing_duplicate_policy() returns trigger language plpgsql as $$
begin
 insert into ged.billing_duplicate_policy_history(tenant_id,updated_by,previous_snapshot,new_snapshot)
 values(new.tenant_id,new.updated_by,case when tg_op='UPDATE' then to_jsonb(old) else null end,to_jsonb(new));
 return new;
end $$;
drop trigger if exists trg_billing_duplicate_policy on ged.billing_duplicate_policy;
create trigger trg_billing_duplicate_policy after insert or update on ged.billing_duplicate_policy
 for each row execute function ged.audit_billing_duplicate_policy();

create table if not exists ged.billing_installment (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, extraction_id uuid not null,
 sequence integer not null check(sequence>0), direction text not null check(direction in ('PAYABLE','RECEIVABLE')),
 due_date date not null, amount numeric(14,2) not null check(amount>0),
 created_by uuid not null, created_at timestamptz not null default now(),
 unique(tenant_id,extraction_id,sequence)
);
create table if not exists ged.billing_settlement (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, installment_id uuid not null,
 amount numeric(14,2) not null check(amount>0), settled_on date not null, reference text not null,
 idempotency_key uuid not null, created_by uuid not null, created_at timestamptz not null default now(),
 reconciled_by uuid null, reconciled_at timestamptz null,
 unique(tenant_id,idempotency_key)
);
create index if not exists ix_billing_settlement_installment on ged.billing_settlement(tenant_id,installment_id);

alter table ged.hospital_billing_document
 add column if not exists revision bigint not null default 0,
 add column if not exists responsible_user_id uuid null,
 add column if not exists priority integer not null default 0,
 add column if not exists appeal_protocol text null,
 add column if not exists appeal_response text null;

-- Register capabilities without granting them to existing roles automatically.
do $$ begin
 if to_regclass('ged.permission') is not null then
  insert into ged.permission(code,name) values
   ('BILLING_VIEW','Consultar faturamento financeiro'),
   ('BILLING_REVIEW','Revisar faturamento financeiro'),
   ('BILLING_MANAGE','Gerenciar extração e regras de faturamento'),
   ('BILLING_SETTLE','Registrar parcelas, liquidações e conciliação'),
   ('BILLING_HOSPITAL_REVIEW','Revisar contas e recursos hospitalares'),
   ('GED_DOCUMENT_MOVE','Mover documentos entre pastas'),
   ('GED_DOCUMENT_MOVE_CONFIDENTIAL','Mover documentos sigilosos')
  on conflict(code) do nothing;
 end if;
end $$;
