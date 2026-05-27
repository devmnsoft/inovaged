-- Colunas das views/tabelas de loans
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name in ('loan_request', 'vw_loan_overdue', 'vw_loan_report')
order by table_name, ordinal_position;

-- Colunas de security/access/failure
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%security%'
    or table_name ilike '%access%'
    or table_name ilike '%failure%'
)
order by table_name, ordinal_position;

-- Enums loans
select t.typname, e.enumlabel
from pg_type t
join pg_enum e on e.enumtypid = t.oid
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged'
and t.typname = 'loan_status'
order by e.enumsortorder;
