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
 event_type varchar(30) not null default 'COLLECTION',
 level text not null check(level in ('FIRST_NOTICE','SECOND_NOTICE','ESCALATED','FINAL_NOTICE')),
 channel text not null default 'INTERNAL', delivery_status text not null default 'PENDING_EXTERNAL',
 message text not null, created_by uuid null, created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
alter table ged.loan_collection_event add column if not exists loan_request_id uuid;
-- Mantém os nomes legados e os nomes ricos sincronizáveis. Bancos anteriores usam
-- loan_id/event_at/kind; instalações novas usam também loan_request_id/created_at/event_type.
alter table ged.loan_collection_event add column if not exists loan_id uuid;
alter table ged.loan_collection_event add column if not exists event_at timestamptz not null default now();
alter table ged.loan_collection_event add column if not exists kind varchar(30) not null default 'COLLECTION';
alter table ged.loan_collection_event add column if not exists event_type varchar(30) not null default 'COLLECTION';
alter table ged.loan_collection_event add column if not exists level text;
alter table ged.loan_collection_event add column if not exists channel text not null default 'INTERNAL';
alter table ged.loan_collection_event add column if not exists delivery_status text not null default 'PENDING_EXTERNAL';
alter table ged.loan_collection_event add column if not exists message text;
alter table ged.loan_collection_event add column if not exists created_by uuid;
alter table ged.loan_collection_event add column if not exists created_at timestamptz not null default now();
alter table ged.loan_collection_event add column if not exists reg_status char(1) not null default 'A';
update ged.loan_collection_event
   set loan_request_id = coalesce(loan_request_id, loan_id),
       loan_id = coalesce(loan_id, loan_request_id),
       created_at = coalesce(created_at, event_at),
       event_at = coalesce(event_at, created_at),
       event_type = coalesce(nullif(event_type, ''), kind, 'COLLECTION'),
       kind = coalesce(nullif(kind, ''), event_type, 'COLLECTION');

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

drop function if exists ged.loan_run_overdue(uuid);
create function ged.loan_run_overdue(p_tenant uuid) returns integer language plpgsql as $$
declare
 v_tenant_id uuid := p_tenant;
 v_count integer := 0;
begin
 if v_tenant_id is null then return 0; end if;
 if exists(select 1 from pg_enum e join pg_type t on t.oid=e.enumtypid join pg_namespace n on n.oid=t.typnamespace where n.nspname='ged' and t.typname='loan_status' and e.enumlabel='OVERDUE') then
  insert into ged.loan_collection_event(tenant_id,loan_id,loan_request_id,event_at,created_at,kind,event_type,level,message)
  select lr.tenant_id,lr.id,lr.id,now(),now(),'OVERDUE','OVERDUE','FIRST_NOTICE','Empréstimo vencido identificado automaticamente.'
    from ged.loan_request lr
   where lr.tenant_id=v_tenant_id and lr.due_at<now() and coalesce(lr.reg_status,'A')='A'
     and upper(lr.status::text) in ('APPROVED','DELIVERED','PREPARING_PHYSICAL','WAITING_PICKUP','DIGITAL_LINK_SENT')
     and not exists (select 1 from ged.loan_collection_event e where e.tenant_id=lr.tenant_id
                      and e.loan_request_id=lr.id and e.event_type='OVERDUE' and coalesce(e.reg_status,'A')='A');
  execute $q$update ged.loan_request set status='OVERDUE'::ged.loan_status,updated_at=now()
    ,last_collection_at=now(),collection_count=coalesce(collection_count,0)+1,collection_level='FIRST_NOTICE'
   where tenant_id=$1 and due_at<now() and coalesce(reg_status,'A')='A'
   and upper(status::text) in ('APPROVED','DELIVERED','PREPARING_PHYSICAL','WAITING_PICKUP','DIGITAL_LINK_SENT')$q$ using v_tenant_id;
  get diagnostics v_count = row_count;
 end if;
 return v_count;
end $$;
