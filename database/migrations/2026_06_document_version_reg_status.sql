create schema if not exists ged;

alter table if exists ged.document_version
add column if not exists reg_status char(1) not null default 'A';

create index if not exists ix_document_version_tenant_document_reg_status
on ged.document_version(tenant_id, document_id, reg_status);
