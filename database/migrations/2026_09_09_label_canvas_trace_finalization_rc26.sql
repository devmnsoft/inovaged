-- RC26: estado aditivo para preparação idempotente e replay fiel de jobs Canvas.
do $$
begin
  if to_regclass('ged.label_print_job_item') is not null then
    alter table ged.label_print_job_item add column if not exists prepared_label_print_id uuid null;
    alter table ged.label_print_job_item add column if not exists prepared_trace_id uuid null;
    alter table ged.label_print_job_item add column if not exists prepared_trace_code varchar(160) null;
    alter table ged.label_print_job_item add column if not exists final_snapshot_json jsonb null;
    alter table ged.label_print_job_item add column if not exists final_snapshot_sha256 varchar(64) null;
    alter table ged.label_print_job_item add column if not exists prepared_at timestamptz null;
  end if;
  if to_regclass('ged.label_print_job') is not null then
    alter table ged.label_print_job add column if not exists prepared_at timestamptz null;
  end if;
end $$;

comment on column ged.label_print_job_item.prepared_trace_code is
  'RC26: uma identidade lógica por origem; Copies reutiliza o mesmo trace/QR.';
