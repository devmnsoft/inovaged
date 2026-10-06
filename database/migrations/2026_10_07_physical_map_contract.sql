-- PhysicalQueries consumes the detailed columns; aggregate readers retain the prefix.
create or replace view ged.vw_physical_map as
select * from ged.vw_physical_map_document_detail;
