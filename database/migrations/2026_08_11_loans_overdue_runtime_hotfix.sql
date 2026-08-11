-- Runtime compatibility for partially migrated loan and physical archive databases.
create schema if not exists ged;

alter table if exists ged.loan_request add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.loan_request_item add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.loan_history add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.loan_request_history add column if not exists reg_status char(1) not null default 'A';

alter type ged.loan_status add value if not exists 'OVERDUE';

create or replace function ged.loan_run_overdue(p_tenant uuid)
returns integer language plpgsql as $$
declare v_count integer := 0;
begin
  update ged.loan_request lr set status = 'OVERDUE'::ged.loan_status
   where lr.tenant_id = p_tenant and coalesce(lr.reg_status, 'A') = 'A'
     and lr.status in ('APPROVED','DELIVERED') and lr.due_at is not null
     and lr.due_at < now() and lr.returned_at is null;
  get diagnostics v_count = row_count;
  insert into ged.loan_collection_event (tenant_id, loan_id, kind, message)
  select lr.tenant_id, lr.id, 'OVERDUE', 'Empréstimo vencido. Cobrança automática gerada.'
    from ged.loan_request lr
   where lr.tenant_id = p_tenant and coalesce(lr.reg_status, 'A') = 'A'
     and lr.status = 'OVERDUE'
     and not exists (select 1 from ged.loan_collection_event e
                      where e.tenant_id = lr.tenant_id and e.loan_id = lr.id and e.kind = 'OVERDUE');
  return coalesce(v_count, 0);
end;
$$;

create or replace view ged.vw_loan_overdue as
select lr.* from ged.loan_request lr
where coalesce(lr.reg_status, 'A') = 'A' and lr.status = 'OVERDUE'::ged.loan_status;

create table if not exists ged.physical_label (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 label_code text not null, title text not null, label_type text not null default 'CUSTOM',
 box_id uuid null, document_id uuid null, location_id uuid null, description text null,
 qr_payload text null, width_mm numeric(8,2) null, height_mm numeric(8,2) null,
 status text not null default 'ACTIVE', created_by uuid null, created_at timestamptz not null default now(),
 updated_by uuid null, updated_at timestamptz null, reg_status char(1) not null default 'A'
);
create unique index if not exists ux_physical_label_tenant_code_active
 on ged.physical_label(tenant_id, upper(label_code)) where reg_status = 'A';

alter table if exists ged.box add column if not exists status text not null default 'ACTIVE';
alter table if exists ged.box add column if not exists capacity_estimated int null;
alter table if exists ged.box add column if not exists last_inventory_at timestamptz null;
alter table if exists ged.box add column if not exists inventory_status text null;
alter table if exists ged.box add column if not exists created_at timestamptz not null default now();
alter table if exists ged.box add column if not exists created_by uuid null;
alter table if exists ged.box add column if not exists updated_at timestamptz null;
alter table if exists ged.box add column if not exists updated_by uuid null;
