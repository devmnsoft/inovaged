select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name in ('audit_log', 'access_failure', 'security_access_failure_log')
order by table_name, ordinal_position;
