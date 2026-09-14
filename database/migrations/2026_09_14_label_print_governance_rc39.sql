-- RC39: governança aditiva da emissão. Seguro para reaplicação; sem alteração destrutiva.
begin;

alter table if exists ged.label_print_job add column if not exists client_action_id uuid null;
alter table if exists ged.label_print_job add column if not exists operation_type varchar(40) not null default 'NEW_LABEL_CURRENT_DATA';
alter table if exists ged.label_print_job add column if not exists artifact_sha256 char(64) null;
alter table if exists ged.label_print_job add column if not exists artifact_content_type varchar(120) null;
alter table if exists ged.label_print_job add column if not exists artifact_size_bytes bigint null;
alter table if exists ged.label_print_job add column if not exists artifact_generated_at timestamptz null;
alter table if exists ged.label_print_job add column if not exists artifact_bytes bytea null;

create unique index if not exists ux_label_print_job_client_action
    on ged.label_print_job(tenant_id, requested_by, client_action_id)
    where client_action_id is not null and reg_status='A';

create index if not exists ix_label_print_job_queue_page
    on ged.label_print_job(tenant_id, requested_at desc, id)
    where reg_status='A';

commit;
