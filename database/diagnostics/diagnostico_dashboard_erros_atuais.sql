-- Colunas loan_request e vw_loan_report
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name in ('loan_request', 'vw_loan_report', 'vw_loan_overdue')
order by table_name, ordinal_position;

-- Colunas security/access failure
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%access%'
    or table_name ilike '%failure%'
    or table_name ilike '%security%'
)
order by table_name, ordinal_position;

-- Colunas app_user
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name = 'app_user'
order by ordinal_position;

-- Colunas audit_log
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name = 'audit_log'
order by ordinal_position;

-- Enums importantes
select t.typname, e.enumlabel
from pg_type t
join pg_enum e on e.enumtypid = t.oid
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged'
and t.typname in ('audit_action_enum', 'loan_status', 'ocr_status_enum', 'document_status_enum')
order by t.typname, e.enumsortorder;
