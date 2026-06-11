-- Histórico idempotente de Loans.
-- Seguro para reexecução: não remove objetos e não apaga dados.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.loan_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid not null,
    old_status text null,
    new_status text null,
    action text not null,
    user_id uuid null,
    user_name text null,
    sector_id uuid null,
    sector_name text null,
    reason text null,
    internal_notes text null,
    metadata_json jsonb not null default '{}'::jsonb,
    correlation_id text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

alter table ged.loan_request_history add column if not exists tenant_id uuid;
alter table ged.loan_request_history add column if not exists loan_request_id uuid;
alter table ged.loan_request_history add column if not exists old_status text null;
alter table ged.loan_request_history add column if not exists new_status text null;
alter table ged.loan_request_history add column if not exists action text;
alter table ged.loan_request_history add column if not exists user_id uuid null;
alter table ged.loan_request_history add column if not exists user_name text null;
alter table ged.loan_request_history add column if not exists sector_id uuid null;
alter table ged.loan_request_history add column if not exists sector_name text null;
alter table ged.loan_request_history add column if not exists reason text null;
alter table ged.loan_request_history add column if not exists internal_notes text null;
alter table ged.loan_request_history add column if not exists metadata_json jsonb not null default '{}'::jsonb;
alter table ged.loan_request_history add column if not exists correlation_id text null;
alter table ged.loan_request_history add column if not exists created_at timestamptz not null default now();
alter table ged.loan_request_history add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.loan_request add column if not exists requester_sector text null;
alter table if exists ged.loan_request add column if not exists sector_id uuid null;
alter table if exists ged.loan_request add column if not exists updated_at timestamptz null;
alter table if exists ged.loan_request add column if not exists updated_by uuid null;

alter table if exists ged.loan_request_item add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.loan_request_item add column if not exists description text null;
alter table if exists ged.loan_request_item add column if not exists reference_code text null;
alter table if exists ged.loan_request_item add column if not exists is_manual boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists document_version_id uuid null;
alter table if exists ged.loan_request_item add column if not exists loan_request_id uuid null;

create index if not exists ix_loan_request_history_tenant_loan_created
on ged.loan_request_history(tenant_id, loan_request_id, created_at desc);

create index if not exists ix_loan_request_history_tenant_action
on ged.loan_request_history(tenant_id, action);

create index if not exists ix_loan_request_history_tenant_user
on ged.loan_request_history(tenant_id, user_id);

do $$
declare
    created_expr text := 'now()';
begin
    if to_regclass('ged.loan_request') is not null then
        if exists (select 1 from information_schema.columns where table_schema='ged' and table_name='loan_request' and column_name='requested_at') then
            created_expr := 'coalesce(l.requested_at, now())';
        elsif exists (select 1 from information_schema.columns where table_schema='ged' and table_name='loan_request' and column_name='created_at') then
            created_expr := 'coalesce(l.created_at, now())';
        end if;

        execute format($sql$
            insert into ged.loan_request_history (
                tenant_id,
                loan_request_id,
                old_status,
                new_status,
                action,
                user_id,
                user_name,
                reason,
                created_at
            )
            select
                l.tenant_id,
                l.id,
                null,
                l.status::text,
                'CREATED',
                null,
                coalesce(l.requester_name, 'Sistema'),
                'Histórico inicial criado pela migração',
                %s
            from ged.loan_request l
            where not exists (
                select 1
                from ged.loan_request_history h
                where h.tenant_id = l.tenant_id
                  and h.loan_request_id = l.id
            );
        $sql$, created_expr);
    end if;
end $$;
