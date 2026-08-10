-- Hotfix final de build/esquema. Seguro para bancos limpos, legados ou parcialmente migrados.
create schema if not exists ged;

-- Preserve a assinatura histórica: CREATE OR REPLACE não permite renomear parâmetros de entrada.
create or replace function ged.loan_run_overdue(p_tenant uuid) returns integer language plpgsql as $$
declare changed integer := 0;
begin
  if p_tenant is null or to_regclass('ged.loan_request') is null then return 0; end if;
  if exists (select 1 from pg_enum e join pg_type t on t.oid=e.enumtypid
             join pg_namespace n on n.oid=t.typnamespace
             where n.nspname='ged' and t.typname='loan_status' and e.enumlabel='OVERDUE') then
    execute $q$update ged.loan_request set status='OVERDUE'::ged.loan_status, updated_at=now()
      where tenant_id=$1 and due_at<now() and coalesce(reg_status,'A')='A'
      and upper(status::text) in ('APPROVED','DELIVERED','PREPARING_PHYSICAL','WAITING_PICKUP','DIGITAL_LINK_SENT')$q$
      using p_tenant;
    get diagnostics changed = row_count;
  end if;
  return changed;
end $$;

-- Normalize somente nomes de colunas de auditoria; os dados legados são preservados.
do $$
declare repair record;
begin
  for repair in select * from (values
    ('document_folder_move_history','moved_at','performed_at'),
    ('box_location_history','changed_at','moved_at'),
    ('box_content_history','changed_at','performed_at'),
    ('label_print','printed_at','performed_at'),
    ('document_classification_audit','created_at','changed_at'),
    ('document_workflow_history','performed_at','created_at')
  ) x(table_name, canonical_name, legacy_name) loop
    if to_regclass('ged.' || repair.table_name) is not null then
      if not exists (select 1 from information_schema.columns where table_schema='ged'
          and table_name=repair.table_name and column_name=repair.canonical_name) then
        execute format('alter table ged.%I add column %I timestamptz', repair.table_name, repair.canonical_name);
      end if;
      if exists (select 1 from information_schema.columns where table_schema='ged'
          and table_name=repair.table_name and column_name=repair.legacy_name) then
        execute format('update ged.%I set %I=coalesce(%I,%I,now()) where %I is null',
          repair.table_name, repair.canonical_name, repair.canonical_name, repair.legacy_name, repair.canonical_name);
      else
        execute format('update ged.%I set %I=now() where %I is null',
          repair.table_name, repair.canonical_name, repair.canonical_name);
      end if;
      execute format('alter table ged.%I alter column %I set default now()', repair.table_name, repair.canonical_name);
    end if;
  end loop;
end $$;

-- Nunca altere ged.batch.status: o trigger tr_batch_status_history depende do tipo atual.
do $$
declare value text;
begin
  if to_regtype('ged.batch_status') is not null then
    foreach value in array array['RECEIVED','TRIAGE','PREPARATION','DIGITIZATION','INDEXING',
      'CONFERENCE','ARCHIVING','FINALIZED','CANCELLED'] loop
      execute format('alter type ged.batch_status add value if not exists %L', value);
    end loop;
  end if;
end $$;

do $$ begin
  if to_regclass('ged.document_folder_move_history') is not null then
    create index if not exists ix_dfmh_tenant_document_moved on ged.document_folder_move_history(tenant_id,document_id,moved_at desc);
  end if;
  if to_regclass('ged.box_location_history') is not null then
    create index if not exists ix_box_location_history_box on ged.box_location_history(tenant_id,box_id,changed_at desc);
  end if;
  if to_regclass('ged.box_content_history') is not null then
    create index if not exists ix_box_content_history_box on ged.box_content_history(tenant_id,box_id,changed_at desc);
  end if;
end $$;
