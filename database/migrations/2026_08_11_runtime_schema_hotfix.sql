-- Runtime compatibility hotfix for legacy and partially migrated InovaGED databases.
-- Additive and idempotent: safe to execute repeatedly before schema validation/workers.
create schema if not exists ged;

alter table if exists ged.box
    add column if not exists status text not null default 'ACTIVE';

update ged.box
   set status = case
       when coalesce(reg_status, 'A') = 'A' then coalesce(nullif(status, ''), 'ACTIVE')
       else 'INACTIVE'
   end;

alter table if exists ged.loan_request
    add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.loan_request_item
    add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.document_acl
    add column if not exists tenant_id uuid;

alter table if exists ged.document_acl
    add column if not exists reg_status char(1) not null default 'A';

do $$
begin
    if to_regclass('ged.document_acl') is not null
       and to_regclass('ged.document') is not null then
        update ged.document_acl a
           set tenant_id = d.tenant_id
          from ged.document d
         where a.document_id = d.id
           and a.tenant_id is null;
    end if;
end $$;

create or replace function ged.loan_run_overdue(p_tenant uuid)
returns integer
language plpgsql
as $$
declare
    v_count integer := 0;
begin
    update ged.loan_request lr
       set status = 'OVERDUE'::ged.loan_status,
           updated_at = now()
     where lr.tenant_id = p_tenant
       and coalesce(lr.reg_status, 'A') = 'A'
       and upper(lr.status::text) in ('APPROVED','DELIVERED','PREPARING_PHYSICAL','WAITING_PICKUP','DIGITAL_LINK_SENT')
       and lr.due_at is not null
       and lr.due_at < now();

    get diagnostics v_count = row_count;

    insert into ged.loan_collection_event
        (tenant_id, loan_id, loan_request_id, event_at, created_at, kind, event_type, level, message)
    select lr.tenant_id, lr.id, lr.id, now(), now(), 'OVERDUE', 'OVERDUE', 'FIRST_NOTICE',
           'Empréstimo vencido. Cobrança automática gerada.'
      from ged.loan_request lr
     where lr.tenant_id = p_tenant
       and coalesce(lr.reg_status, 'A') = 'A'
       and upper(lr.status::text) = 'OVERDUE'
       and not exists (
           select 1
             from ged.loan_collection_event e
            where e.tenant_id = lr.tenant_id
              and coalesce(e.loan_request_id, e.loan_id) = lr.id
              and coalesce(e.event_type, e.kind) = 'OVERDUE'
              and coalesce(e.reg_status, 'A') = 'A'
       );

    return coalesce(v_count, 0);
end $$;
