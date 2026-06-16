create schema if not exists ged;

alter table ged.upload_batch add column if not exists created_by uuid null;
alter table ged.upload_batch add column if not exists created_by_name text null;
alter table ged.upload_batch add column if not exists folder_id uuid null;
alter table ged.upload_batch add column if not exists requested_folder_id uuid null;
alter table ged.upload_batch add column if not exists status text not null default 'OPEN';
alter table ged.upload_batch add column if not exists total_files int not null default 0;
alter table ged.upload_batch add column if not exists success_files int not null default 0;
alter table ged.upload_batch add column if not exists failed_files int not null default 0;
alter table ged.upload_batch add column if not exists skipped_files int not null default 0;
alter table ged.upload_batch add column if not exists source_ip text null;
alter table ged.upload_batch add column if not exists user_agent text null;
alter table ged.upload_batch add column if not exists correlation_id text null;
alter table ged.upload_batch add column if not exists started_at timestamptz null;
alter table ged.upload_batch add column if not exists finished_at timestamptz null;
alter table ged.upload_batch add column if not exists created_at timestamptz not null default now();
alter table ged.upload_batch add column if not exists updated_at timestamptz null;
alter table ged.upload_batch add column if not exists reg_status char(1) not null default 'A';
alter table ged.upload_batch add column if not exists acknowledged_at timestamptz null;
alter table ged.upload_batch add column if not exists acknowledged_by uuid null;
alter table ged.upload_batch add column if not exists problem_seen boolean not null default false;
alter table ged.upload_batch add column if not exists user_notes text null;
alter table ged.upload_batch add column if not exists options_json jsonb not null default '{}'::jsonb;

update ged.upload_batch
set created_by_name = coalesce(nullif(created_by_name, ''), created_by::text, 'Usuário não identificado')
where created_by_name is null
   or trim(created_by_name) = '';

create index if not exists ix_upload_batch_tenant_created
on ged.upload_batch(tenant_id, created_at desc);

create index if not exists ix_upload_batch_tenant_created_by
on ged.upload_batch(tenant_id, created_by, created_at desc);

create index if not exists ix_upload_batch_tenant_status
on ged.upload_batch(tenant_id, status, created_at desc);

alter table ged.upload_batch_item add column if not exists uploaded_by_name text null;
alter table ged.upload_batch_item add column if not exists processing_warning text null;
alter table ged.upload_batch_item add column if not exists error_step text null;
alter table ged.upload_batch_item add column if not exists can_retry boolean not null default false;
alter table ged.upload_batch_item add column if not exists elapsed_ms bigint null;
alter table ged.upload_batch_item add column if not exists finished_at timestamptz null;
alter table ged.upload_batch_item add column if not exists updated_at timestamptz null;
