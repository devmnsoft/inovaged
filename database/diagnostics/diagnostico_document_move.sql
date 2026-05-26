-- Tabelas de documentos e colunas
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%document%'
    or table_name ilike '%documento%'
)
order by table_name, ordinal_position;

-- Tabelas de pastas e colunas
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%folder%'
    or table_name ilike '%pasta%'
)
order by table_name, ordinal_position;

-- Procurar colunas candidatas de pasta
select table_schema, table_name, column_name, data_type
from information_schema.columns
where table_schema = 'ged'
and (
    column_name ilike '%folder%'
    or column_name ilike '%pasta%'
    or column_name ilike '%parent%'
)
order by table_name, ordinal_position;

-- Procurar colunas de exclusão/status
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    column_name ilike '%delete%'
    or column_name ilike '%deleted%'
    or column_name ilike '%status%'
    or column_name ilike '%reg_status%'
)
order by table_name, ordinal_position;
