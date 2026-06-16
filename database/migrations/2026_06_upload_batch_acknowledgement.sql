-- Persistência de reconhecimento operacional de lotes de upload GED.
alter table ged.upload_batch
add column if not exists acknowledged_at timestamptz null;

alter table ged.upload_batch
add column if not exists acknowledged_by uuid null;

alter table ged.upload_batch
add column if not exists user_notes text null;

alter table ged.upload_batch
add column if not exists problem_seen boolean not null default false;

create index if not exists ix_upload_batch_last_problem_user
on ged.upload_batch(tenant_id, created_by, created_at desc)
where coalesce(reg_status,'A')='A';
