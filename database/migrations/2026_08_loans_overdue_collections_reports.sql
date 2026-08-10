-- Empréstimos operacionais: vencimento, cobrança, renovação e relatórios (idempotente/aditivo).
create schema if not exists ged;

alter table if exists ged.loan_request add column if not exists last_collection_at timestamptz null;
alter table if exists ged.loan_request add column if not exists collection_count integer not null default 0;
alter table if exists ged.loan_request add column if not exists collection_level text null;
alter table if exists ged.loan_request add column if not exists renewed_at timestamptz null;
alter table if exists ged.loan_request add column if not exists renewed_by uuid null;
alter table if exists ged.loan_request add column if not exists renewal_reason text null;
alter table if exists ged.loan_request add column if not exists previous_due_at timestamptz null;

create table if not exists ged.loan_collection_event (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, loan_request_id uuid not null,
 level text not null check(level in ('FIRST_NOTICE','SECOND_NOTICE','ESCALATED','FINAL_NOTICE')),
 channel text not null default 'INTERNAL', delivery_status text not null default 'PENDING_EXTERNAL',
 message text not null, created_by uuid null, created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
alter table ged.loan_collection_event add column if not exists loan_request_id uuid;
alter table ged.loan_collection_event add column if not exists level text;
alter table ged.loan_collection_event add column if not exists channel text not null default 'INTERNAL';
alter table ged.loan_collection_event add column if not exists delivery_status text not null default 'PENDING_EXTERNAL';
alter table ged.loan_collection_event add column if not exists message text;
alter table ged.loan_collection_event add column if not exists created_by uuid;
alter table ged.loan_collection_event add column if not exists created_at timestamptz not null default now();
alter table ged.loan_collection_event add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.loan_report_run (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, run_by uuid null,
 filters_json jsonb not null default '{}'::jsonb, row_count integer not null default 0,
 started_at timestamptz not null default now(), finished_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create index if not exists ix_loan_request_due_open on ged.loan_request(tenant_id,due_at) where coalesce(reg_status,'A')='A';
create index if not exists ix_loan_request_status_period on ged.loan_request(tenant_id,status,requested_at desc);
create index if not exists ix_loan_request_requester_period on ged.loan_request(tenant_id,requester_id,requested_at desc);
create index if not exists ix_loan_request_sector_period on ged.loan_request(tenant_id,requester_sector_name,requested_at desc);
create index if not exists ix_loan_collection_loan_created on ged.loan_collection_event(tenant_id,loan_request_id,created_at desc);
create index if not exists ix_loan_report_run_tenant_started on ged.loan_report_run(tenant_id,started_at desc);

create or replace function ged.loan_run_overdue(p_tenant_id uuid) returns integer language plpgsql as $$
declare changed integer := 0;
begin
 if p_tenant_id is null then return 0; end if;
 if exists(select 1 from pg_enum e join pg_type t on t.oid=e.enumtypid join pg_namespace n on n.oid=t.typnamespace where n.nspname='ged' and t.typname='loan_status' and e.enumlabel='OVERDUE') then
  execute $q$update ged.loan_request set status='OVERDUE'::ged.loan_status,updated_at=now()
   where tenant_id=$1 and due_at<now() and coalesce(reg_status,'A')='A'
   and upper(status::text) in ('APPROVED','DELIVERED','PREPARING_PHYSICAL','WAITING_PICKUP','DIGITAL_LINK_SENT')$q$ using p_tenant_id;
  get diagnostics changed = row_count;
 end if;
 return changed;
end $$;
