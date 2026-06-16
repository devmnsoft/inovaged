create schema if not exists ged;

alter table ged.document add column if not exists is_document_incomplete boolean not null default false;
alter table ged.document add column if not exists incomplete_reason text null;
alter table ged.document add column if not exists incomplete_source text null;
alter table ged.document add column if not exists deleted_at timestamptz null;
alter table ged.document add column if not exists deleted_by uuid null;
alter table ged.document add column if not exists deleted_reason text null;
alter table ged.document add column if not exists updated_at timestamptz null;
alter table ged.document add column if not exists updated_by uuid null;

alter table ged.document_version add column if not exists is_document_incomplete boolean not null default false;
alter table ged.document_version add column if not exists incomplete_reason text null;
alter table ged.document_version add column if not exists incomplete_source text null;

alter table ged.upload_batch add column if not exists finished_at timestamptz null;
alter table ged.upload_batch add column if not exists updated_at timestamptz null;
alter table ged.upload_batch add column if not exists source_ip text null;
alter table ged.upload_batch add column if not exists user_agent text null;
alter table ged.upload_batch add column if not exists correlation_id text null;
alter table ged.upload_batch add column if not exists options_json jsonb not null default '{}'::jsonb;

alter table ged.upload_batch_item add column if not exists error_step text null;
alter table ged.upload_batch_item add column if not exists can_retry boolean not null default false;
alter table ged.upload_batch_item add column if not exists finished_at timestamptz null;
alter table ged.upload_batch_item add column if not exists elapsed_ms bigint null;
alter table ged.upload_batch_item add column if not exists processing_warning text null;
alter table ged.upload_batch_item add column if not exists updated_at timestamptz null;

create index if not exists ix_document_tenant_incomplete on ged.document(tenant_id, is_document_incomplete) where coalesce(reg_status,'A')='A';
create index if not exists ix_document_tenant_deleted on ged.document(tenant_id, deleted_at) where deleted_at is not null;
create index if not exists ix_upload_batch_tenant_created on ged.upload_batch(tenant_id, created_at desc);
create index if not exists ix_upload_batch_item_tenant_batch_status on ged.upload_batch_item(tenant_id, batch_id, status);
create index if not exists ix_upload_batch_item_retry on ged.upload_batch_item(tenant_id, batch_id, can_retry, status) where can_retry = true;
