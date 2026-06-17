create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.code_sequence (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    entity_name text not null,
    prefix text not null,
    current_value bigint not null default 0,
    padding int not null default 4,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);

alter table ged.code_sequence add column if not exists tenant_id uuid;
alter table ged.code_sequence add column if not exists entity_name text;
alter table ged.code_sequence add column if not exists prefix text;
alter table ged.code_sequence alter column prefix set default 'COD';
update ged.code_sequence set prefix='COD' where prefix is null;
alter table ged.code_sequence alter column prefix set not null;
alter table ged.code_sequence add column if not exists current_value bigint not null default 0;
alter table ged.code_sequence add column if not exists padding int not null default 4;
alter table ged.code_sequence add column if not exists created_at timestamptz not null default now();
alter table ged.code_sequence add column if not exists updated_at timestamptz null;
alter table ged.code_sequence add column if not exists reg_status char(1) not null default 'A';

create unique index if not exists ux_code_sequence_tenant_entity
on ged.code_sequence(tenant_id, entity_name)
where coalesce(reg_status,'A')='A';

create index if not exists ix_code_sequence_tenant
on ged.code_sequence(tenant_id);

insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding)
select distinct tenant_id, 'ParameterItem:LOTACAO', 'LOT', 0, 4
from ged.parameter_category
where code='LOTACAO'
on conflict do nothing;

insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding)
select distinct tenant_id, 'ParameterItem:TIPO_DOCUMENTO', 'TIP', 0, 4
from ged.parameter_category
where code='TIPO_DOCUMENTO'
on conflict do nothing;

insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding)
select distinct tenant_id, 'ParameterItem:CLASSIFICACAO', 'CLA', 0, 4
from ged.parameter_category
where code='CLASSIFICACAO'
on conflict do nothing;

do $$
begin
    if to_regclass('ged.schema_migration_history') is not null then
        insert into ged.schema_migration_history(script_name, notes)
        values ('2026_06_code_sequence.sql', 'Tabela de sequência de códigos automáticos')
        on conflict (script_name) do update
        set applied_at = now(),
            success = true,
            notes = excluded.notes;
    end if;
end $$;
