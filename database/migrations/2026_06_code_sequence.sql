create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.code_sequence (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    entity_name text not null,
    prefix text null,
    current_value bigint not null default 0,
    padding int not null default 4,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique(tenant_id, entity_name)
);

create index if not exists ix_code_sequence_tenant_entity
on ged.code_sequence(tenant_id, entity_name);

do $$
begin
    if exists (select 1 from pg_type t join pg_namespace n on n.oid = t.typnamespace where n.nspname = 'ged' and t.typname = 'audit_action_enum')
       and not exists (
           select 1
           from pg_enum e
           join pg_type t on t.oid = e.enumtypid
           join pg_namespace n on n.oid = t.typnamespace
           where n.nspname = 'ged'
             and t.typname = 'audit_action_enum'
             and e.enumlabel = 'CODE_GENERATED'
       ) then
        alter type ged.audit_action_enum add value 'CODE_GENERATED';
    end if;
end $$;
