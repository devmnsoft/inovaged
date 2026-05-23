-- Ver tabelas GED relevantes
select table_schema, table_name
from information_schema.tables
where table_schema = 'ged'
order by table_name;

-- Ver colunas de documentos
select table_schema, table_name, column_name, data_type
from information_schema.columns
where table_schema = 'ged'
and table_name ilike '%document%'
order by table_name, ordinal_position;

-- Ver colunas de loans
select table_schema, table_name, column_name, data_type
from information_schema.columns
where table_schema = 'ged'
and table_name ilike '%loan%'
order by table_name, ordinal_position;

-- Ver colunas de solicitações
select table_schema, table_name, column_name, data_type
from information_schema.columns
where table_schema = 'ged'
and table_name ilike '%solicit%'
order by table_name, ordinal_position;

-- Ver enums existentes
select t.typname, e.enumlabel
from pg_type t
join pg_enum e on e.enumtypid = t.oid
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged'
order by t.typname, e.enumsortorder;
