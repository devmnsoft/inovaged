begin;
create table if not exists ged.hospital_billing_review_history(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, hospital_billing_id uuid not null,
 reviewed_by uuid not null, review_status text not null, denial_status text null,
 approved_amount numeric(18,2) not null default 0, denied_amount numeric(18,2) not null default 0,
 recovered_amount numeric(18,2) not null default 0, notes varchar(1000) null, reviewed_at timestamptz not null default now(),
 constraint ck_hospital_review_status check(review_status in ('PENDING_REVIEW','APPROVED','DIVERGENT')),
 constraint ck_hospital_review_amounts check(approved_amount>=0 and denied_amount>=0 and recovered_amount>=0 and recovered_amount<=denied_amount)
);
create index if not exists ix_hospital_billing_review_history on ged.hospital_billing_review_history(tenant_id,hospital_billing_id,reviewed_at desc);
commit;
