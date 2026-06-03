select
    table_schema,
    table_name,
    column_name,
    data_type
from information_schema.columns
where table_schema = 'ged'
  and table_name in (
      'app_audit_log',
      'audit_log',
      'system_log',
      'vw_system_logs',
      'app_user',
      'users'
  )
order by table_name, ordinal_position;
