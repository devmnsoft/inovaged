-- Evolução idempotente do módulo de empréstimos para itens manuais, escopo por setor e histórico rico.
do $$
begin
    if not exists (select 1 from pg_type t join pg_namespace n on n.oid=t.typnamespace where n.nspname='ged' and t.typname='loan_status') then
        create type ged.loan_status as enum ('REQUESTED','APPROVED','REJECTED','DELIVERED','RETURNED','OVERDUE','CANCELLED','RETURNED_FOR_ADJUSTMENT');
    else
        alter type ged.loan_status add value if not exists 'REJECTED';
        alter type ged.loan_status add value if not exists 'RETURNED_FOR_ADJUSTMENT';
    end if;
end $$;

alter table if exists ged.loan_request add column if not exists requester_sector text null;
alter table if exists ged.loan_request alter column document_id drop not null;

create table if not exists ged.loan_request_item (
    id uuid not null default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid null,
    loan_id uuid null,
    document_id uuid null,
    document_version_id uuid null,
    is_manual boolean not null default false,
    is_physical boolean not null default false,
    reference_code text null,
    description text null,
    document_type text null,
    patient_name text null,
    medical_record_number text null,
    box_code text null,
    physical_location text null,
    notes text null,
    created_at timestamptz not null default now(),
    reg_date timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

alter table if exists ged.loan_request_item add column if not exists id uuid null;
update ged.loan_request_item set id = gen_random_uuid() where id is null;
alter table if exists ged.loan_request_item alter column id set default gen_random_uuid();
alter table if exists ged.loan_request_item alter column id set not null;
alter table if exists ged.loan_request_item add column if not exists loan_request_id uuid null;
alter table if exists ged.loan_request_item add column if not exists loan_id uuid null;
update ged.loan_request_item set loan_request_id = coalesce(loan_request_id, loan_id), loan_id = coalesce(loan_id, loan_request_id);
alter table if exists ged.loan_request_item alter column document_id drop not null;
alter table if exists ged.loan_request_item add column if not exists document_version_id uuid null;
alter table if exists ged.loan_request_item add column if not exists is_manual boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists reference_code text null;
alter table if exists ged.loan_request_item add column if not exists description text null;
alter table if exists ged.loan_request_item add column if not exists document_type text null;
alter table if exists ged.loan_request_item add column if not exists patient_name text null;
alter table if exists ged.loan_request_item add column if not exists medical_record_number text null;
alter table if exists ged.loan_request_item add column if not exists box_code text null;
alter table if exists ged.loan_request_item add column if not exists physical_location text null;
alter table if exists ged.loan_request_item add column if not exists notes text null;
alter table if exists ged.loan_request_item add column if not exists created_at timestamptz not null default now();

do $$
begin
    if exists (select 1 from information_schema.table_constraints where table_schema='ged' and table_name='loan_request_item' and constraint_type='PRIMARY KEY' and constraint_name='loan_request_item_pkey') then
        alter table if exists ged.loan_request_item drop constraint loan_request_item_pkey;
    end if;
    if not exists (select 1 from information_schema.table_constraints where table_schema='ged' and table_name='loan_request_item' and constraint_name='pk_loan_request_item') then
        alter table if exists ged.loan_request_item add constraint pk_loan_request_item primary key (id);
    end if;
end $$;

create index if not exists ix_loan_request_item_request on ged.loan_request_item(tenant_id, loan_request_id, reg_status);
create index if not exists ix_loan_request_requester_sector on ged.loan_request(tenant_id, requester_sector, requested_at desc) where reg_status='A';

create table if not exists ged.loan_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid not null,
    old_status text null,
    new_status text null,
    action text not null,
    user_id uuid null,
    user_name text null,
    sector_id text null,
    reason text null,
    internal_notes text null,
    created_at timestamptz not null default now(),
    correlation_id text null
);

create index if not exists ix_loan_request_history_request on ged.loan_request_history(tenant_id, loan_request_id, created_at desc);

do $$
begin
    if to_regclass('ged.loan_request') is not null then
        update ged.loan_request lr
           set requester_sector = nullif(coalesce(s.setor, s.lotacao, ''), '')
        from ged.app_user u
        left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id
        where lr.tenant_id=u.tenant_id
          and lr.requester_id=u.id
          and lr.requester_sector is null;
    end if;
end $$;
