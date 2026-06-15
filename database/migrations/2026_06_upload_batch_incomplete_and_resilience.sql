create schema if not exists ged;

alter table ged.upload_batch
add column if not exists options_json jsonb not null default '{}'::jsonb;

alter table ged.upload_batch
add column if not exists updated_at timestamptz null;

alter table ged.upload_batch_item
add column if not exists upload_client_id text null;

alter table ged.upload_batch_item
add column if not exists content_hash text null;

alter table ged.upload_batch_item
add column if not exists mark_as_incomplete boolean not null default false;

alter table ged.upload_batch_item
add column if not exists incomplete_reason text null;

alter table ged.upload_batch_item
add column if not exists retry_after_at timestamptz null;

alter table ged.upload_batch_item
add column if not exists updated_at timestamptz null;

alter table ged.document
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document
add column if not exists incomplete_reason text null;

alter table ged.document
add column if not exists incomplete_source text null;

alter table ged.document_version
add column if not exists is_document_incomplete boolean not null default false;

alter table ged.document_version
add column if not exists incomplete_reason text null;

alter table ged.document_version
add column if not exists incomplete_source text null;

create index if not exists ix_upload_batch_item_tenant_batch_status
on ged.upload_batch_item(tenant_id, batch_id, status);

create index if not exists ix_upload_batch_item_tenant_hash
on ged.upload_batch_item(tenant_id, batch_id, content_hash);

create unique index if not exists ux_upload_batch_item_dedup
on ged.upload_batch_item(tenant_id, batch_id, original_file_name, size_bytes, content_hash)
where coalesce(reg_status,'A')='A'
  and content_hash is not null;

create index if not exists ix_document_incomplete
on ged.document(tenant_id, is_document_incomplete)
where coalesce(reg_status,'A')='A';
