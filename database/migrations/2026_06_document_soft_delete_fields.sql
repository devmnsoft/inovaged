-- InovaGED - campos padronizados para exclusão lógica de documentos GED.
-- Idempotente: pode ser executado repetidas vezes, sem apagar dados e sem DROP.

create schema if not exists ged;

alter table ged.document
add column if not exists reg_status char(1) not null default 'A';

alter table ged.document
add column if not exists deleted_at timestamptz null;

alter table ged.document
add column if not exists deleted_by uuid null;

alter table ged.document
add column if not exists deleted_reason text null;

alter table ged.document
add column if not exists updated_at timestamptz null;

alter table ged.document
add column if not exists updated_by uuid null;

create index if not exists ix_document_tenant_reg_status
on ged.document(tenant_id, reg_status);

create index if not exists ix_document_tenant_folder_reg_status
on ged.document(tenant_id, folder_id, reg_status);

create index if not exists ix_document_deleted_at
on ged.document(deleted_at)
where deleted_at is not null;
