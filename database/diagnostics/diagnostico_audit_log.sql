select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name = 'audit_log'
order by ordinal_position;

select t.typname, e.enumlabel
from pg_type t
join pg_enum e on e.enumtypid = t.oid
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged'
and t.typname = 'audit_action_enum'
order by e.enumsortorder;
