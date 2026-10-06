-- Compatibility for the canonical one-classification-per-document legacy table.
-- Preserve its document_id primary key and every existing classification.
alter table if exists ged.document_classification
    add column if not exists id uuid not null default gen_random_uuid();

-- Preserve dependencies of the old physical map rather than dropping it CASCADE.
do $$
begin
    if exists (select 1 from information_schema.columns where table_schema='ged'
        and table_name='vw_physical_map' and ordinal_position=2 and column_name='document_id') then
        alter view ged.vw_physical_map rename to vw_physical_map_pre_archival;
    end if;
end $$;
