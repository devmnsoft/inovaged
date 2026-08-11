-- Regras de integridade e trilha imutável para a conferência financeira.
create table if not exists ged.billing_extraction_review_history (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, extraction_id uuid not null,
 old_status text not null, new_status text not null, reviewed_by uuid null, reviewed_at timestamptz not null default now(),
 snapshot_json jsonb not null, reg_status char(1) not null default 'A'
);
create index if not exists ix_billing_review_history_extraction
 on ged.billing_extraction_review_history(tenant_id, extraction_id, reviewed_at desc);

create or replace function ged.audit_billing_extraction_review() returns trigger language plpgsql as $$
begin
 if new.extraction_status is distinct from old.extraction_status and new.extraction_status in ('APPROVED','DIVERGENT') then
  insert into ged.billing_extraction_review_history(tenant_id, extraction_id, old_status, new_status, reviewed_by, snapshot_json)
  values(new.tenant_id, new.id, old.extraction_status, new.extraction_status, new.reviewed_by, to_jsonb(new) - 'extracted_json');
 end if;
 return new;
end $$;

drop trigger if exists trg_billing_extraction_review on ged.billing_document_extraction;
create trigger trg_billing_extraction_review after update on ged.billing_document_extraction
 for each row execute function ged.audit_billing_extraction_review();

do $$ begin
 alter table ged.billing_document_extraction add constraint ck_billing_extraction_status
  check (extraction_status in ('PENDING','PENDING_REVIEW','APPROVED','DIVERGENT')) not valid;
exception when duplicate_object then null; end $$;
do $$ begin
 alter table ged.billing_document_extraction add constraint ck_billing_extraction_nonnegative
  check (gross_amount >= 0 and net_amount >= 0 and tax_amount >= 0 and ust_quantity >= 0 and ust_unit_value >= 0) not valid;
exception when duplicate_object then null; end $$;
