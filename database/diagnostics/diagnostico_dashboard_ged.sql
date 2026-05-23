-- Tabelas no schema GED
select table_schema, table_name
from information_schema.tables
where table_schema = 'ged'
order by table_name;

-- Colunas das tabelas de documentos
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%document%'
    or table_name ilike '%documento%'
)
order by table_name, ordinal_position;

-- Colunas de solicitações
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name ilike '%solicit%'
order by table_name, ordinal_position;

-- Colunas de loans
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name ilike '%loan%'
order by table_name, ordinal_position;

-- Enums GED
select t.typname, e.enumlabel
from pg_type t
join pg_enum e on e.enumtypid = t.oid
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged'
order by t.typname, e.enumsortorder;
