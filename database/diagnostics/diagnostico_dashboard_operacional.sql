-- Tabelas GED
select table_schema, table_name
from information_schema.tables
where table_schema = 'ged'
order by table_name;

-- Colunas documentos
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name in ('document', 'documents', 'document_classification')
order by table_name, ordinal_position;

-- Colunas OCR
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%ocr%'
    or table_name ilike '%preview%'
)
order by table_name, ordinal_position;

-- Colunas Loans
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%loan%'
)
order by table_name, ordinal_position;

-- Colunas auditoria/acesso
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%audit%'
    or table_name ilike '%access%'
    or table_name ilike '%failure%'
)
order by table_name, ordinal_position;

-- Enums GED
select t.typname, e.enumlabel
from pg_type t
join pg_enum e on e.enumtypid = t.oid
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged'
order by t.typname, e.enumsortorder;
