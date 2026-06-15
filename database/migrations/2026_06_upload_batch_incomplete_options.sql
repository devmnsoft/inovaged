alter table ged.upload_batch
add column if not exists options_json jsonb not null default '{}'::jsonb;

alter table ged.upload_batch_item
add column if not exists mark_as_incomplete boolean not null default false;

alter table ged.upload_batch_item
add column if not exists incomplete_reason text null;

alter table ged.document
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document
add column if not exists incomplete_reason text null;

alter table ged.document_version
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document_version
add column if not exists incomplete_reason text null;

alter table ged.document
add column if not exists incomplete_source text null;

alter table ged.document_version
add column if not exists incomplete_source text null;
