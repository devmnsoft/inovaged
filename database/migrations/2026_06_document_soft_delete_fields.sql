-- Campos padronizados para exclusão lógica de documentos GED.
alter table ged.document
add column if not exists reg_status char(1) not null default 'A';

alter table ged.document
add column if not exists deleted_at timestamptz null;

alter table ged.document
add column if not exists deleted_at_utc timestamptz null;

alter table ged.document
add column if not exists deleted_by uuid null;
