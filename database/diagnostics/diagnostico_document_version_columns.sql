select
    table_schema,
    table_name,
    column_name,
    data_type
from information_schema.columns
where table_schema = 'ged'
and table_name ilike '%version%'
order by table_name, ordinal_position;
