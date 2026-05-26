select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    table_name ilike '%ocr%'
    or table_name ilike '%preview%'
    or table_name ilike '%version%'
    or table_name ilike '%document%'
)
order by table_name, ordinal_position;

select t.typname, e.enumlabel
from pg_type t
join pg_enum e on e.enumtypid = t.oid
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged'
and t.typname ilike '%ocr%'
order by t.typname, e.enumsortorder;
