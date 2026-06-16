create schema if not exists ged;

alter table ged.upload_batch
add column if not exists created_by_name text null;

alter table ged.upload_batch_item
add column if not exists uploaded_by_name text null;

update ged.upload_batch
set created_by_name = coalesce(created_by_name, created_by::text)
where created_by_name is null
  and created_by is not null;

create index if not exists ix_upload_batch_tenant_created_by
on ged.upload_batch(tenant_id, created_by, created_at desc);
