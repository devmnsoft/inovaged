create schema if not exists ged;

do $$
declare
    description_fallback text;
begin
    if to_regclass('ged.permission') is not null then
        alter table ged.permission
            add column if not exists description text;

        alter table ged.permission
            add column if not exists module text;

        description_fallback := case
            when exists (
                select 1 from information_schema.columns
                where table_schema = 'ged' and table_name = 'permission' and column_name = 'code'
            ) and exists (
                select 1 from information_schema.columns
                where table_schema = 'ged' and table_name = 'permission' and column_name = 'id'
            ) then 'coalesce(nullif(description, ''''), code::text, id::text)'
            when exists (
                select 1 from information_schema.columns
                where table_schema = 'ged' and table_name = 'permission' and column_name = 'code'
            ) then 'coalesce(nullif(description, ''''), code::text)'
            when exists (
                select 1 from information_schema.columns
                where table_schema = 'ged' and table_name = 'permission' and column_name = 'id'
            ) then 'coalesce(nullif(description, ''''), id::text)'
            else 'coalesce(nullif(description, ''''), ''Permissão sem descrição'')'
        end;

        execute format(
            'update ged.permission set description = %s where description is null or btrim(description) = ''''',
            description_fallback
        );

        update ged.permission
        set module = coalesce(nullif(module, ''), 'Geral')
        where module is null or btrim(module) = '';
    end if;
end $$;
