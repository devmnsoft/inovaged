select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_name ilike '%role%'
   or table_name ilike '%user%'
   or table_name ilike '%perfil%'
   or table_name ilike '%permission%'
order by table_schema, table_name, ordinal_position;
