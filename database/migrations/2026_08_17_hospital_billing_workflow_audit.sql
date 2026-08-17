begin;
create schema if not exists ged;

alter table if exists ged.hospital_billing_review_history
  add column if not exists previous_review_status text null,
  add column if not exists previous_denial_status text null,
  add column if not exists changed_fields jsonb not null default '{}'::jsonb;

do $$
begin
  if to_regclass('ged.hospital_billing_review_history') is not null then
    alter table ged.hospital_billing_review_history drop constraint if exists ck_hospital_review_status;
    alter table ged.hospital_billing_review_history add constraint ck_hospital_review_status
      check(review_status in ('PENDING_REVIEW','APPROVED','DIVERGENT','DENIED','APPEAL_IN_REVIEW','RECOVERED','CLOSED')) not valid;
  end if;
end $$;

create index if not exists ix_hospital_billing_appeal_deadline
on ged.hospital_billing_document(tenant_id,due_date)
where reg_status='A' and denied_amount>0 and coalesce(denial_status,'') not in ('RECOVERED','CLOSED');
commit;
