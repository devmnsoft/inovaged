-- The next published script creates an aggregate view with fewer columns.
-- Keep the detailed contract and its dependencies available without DROP CASCADE.
do $$
begin
    if exists (select 1 from information_schema.columns where table_schema='ged'
        and table_name='vw_physical_map' and column_name='document_title') then
        alter view ged.vw_physical_map rename to vw_physical_map_document_detail;
    end if;
end $$;
