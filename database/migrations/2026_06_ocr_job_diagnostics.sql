create schema if not exists ged;

alter table ged.ocr_job
add column if not exists error_message text null;

alter table ged.ocr_job
add column if not exists error_details_json jsonb null;

alter table ged.ocr_job
add column if not exists started_at timestamptz null;

alter table ged.ocr_job
add column if not exists finished_at timestamptz null;

alter table ged.ocr_job
add column if not exists attempt_count int not null default 0;

alter table ged.ocr_job
add column if not exists worker_id text null;

alter table ged.ocr_job
add column if not exists locked_at timestamptz null;

alter table ged.ocr_job
add column if not exists locked_by text null;

alter table ged.ocr_job
add column if not exists updated_at timestamptz null;

alter table ged.ocr_job
add column if not exists next_attempt_at timestamptz null;

alter table ged.ocr_job
add column if not exists failure_code text null;

alter table ged.ocr_job
add column if not exists reg_status char(1) not null default 'A';

create index if not exists ix_ocr_job_tenant_status_requested
on ged.ocr_job(tenant_id, status, requested_at desc);

create index if not exists ix_ocr_job_tenant_version
on ged.ocr_job(tenant_id, document_version_id);

create index if not exists ix_ocr_job_tenant_failure_code
on ged.ocr_job(tenant_id, failure_code)
where failure_code is not null;

create index if not exists ix_ocr_job_tenant_next_attempt
on ged.ocr_job(tenant_id, next_attempt_at)
where next_attempt_at is not null;

create index if not exists ix_ocr_job_tenant_worker_lock
on ged.ocr_job(tenant_id, locked_by, locked_at)
where locked_by is not null;

do $$
begin
    if to_regclass('ged.schema_migration_history') is not null then
        insert into ged.schema_migration_history(script_name, notes)
        values ('2026_06_ocr_job_diagnostics.sql', 'Campos de diagnóstico técnico e retry do OCR Job')
        on conflict (script_name) do update
        set applied_at = now(),
            success = true,
            notes = excluded.notes;
    end if;
end $$;
