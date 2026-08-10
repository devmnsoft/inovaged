-- Hotfix definitivo de compatibilidade arquivística (aditivo, idempotente e sem perda de dados).
-- Suporta o dump legado, instalações novas e execuções de agosto interrompidas.
create schema if not exists ged;

alter table if exists ged.label_print add column if not exists printed_at timestamptz default now();
alter table if exists ged.label_print add column if not exists label_type varchar(30);
alter table if exists ged.label_print add column if not exists snapshot_json jsonb;
alter table if exists ged.label_print add column if not exists payload_hash_sha256 char(64);
alter table if exists ged.label_print add column if not exists template_version varchar(60);

alter table if exists ged.document_folder_move_history add column if not exists moved_at timestamptz default now();
alter table if exists ged.box_location_history add column if not exists old_location_id uuid;
alter table if exists ged.box_location_history add column if not exists new_location_id uuid;
alter table if exists ged.box_location_history add column if not exists changed_at timestamptz default now();
alter table if exists ged.box_location_history add column if not exists changed_by uuid;
alter table if exists ged.box_location_history add column if not exists notes text;
alter table if exists ged.box_location_history add column if not exists data jsonb;
alter table if exists ged.box_location_history add column if not exists reg_status char(1) default 'A';
alter table if exists ged.box_content_history add column if not exists changed_at timestamptz default now();
alter table if exists ged.batch_history add column if not exists changed_at timestamptz default now();
alter table if exists ged.batch_history add column if not exists event_time timestamptz default now();
alter table if exists ged.batch_history add column if not exists event_type text default 'STATUS_CHANGED';
alter table if exists ged.batch_history add column if not exists notes text;
alter table if exists ged.batch_history add column if not exists data jsonb;
alter table if exists ged.classification_plan_history add column if not exists changed_at timestamptz default now();
alter table if exists ged.document_classification_audit add column if not exists created_at timestamptz default now();
alter table if exists ged.document_workflow_history add column if not exists performed_at timestamptz default now();

do $$
begin
 if exists (select 1 from pg_type t join pg_namespace n on n.oid=t.typnamespace
            where n.nspname='ged' and t.typname='batch_status') then
  alter type ged.batch_status add value if not exists 'PREPARATION';
  alter type ged.batch_status add value if not exists 'CONFERENCE';
  alter type ged.batch_status add value if not exists 'ARCHIVING';
  alter type ged.batch_status add value if not exists 'FINALIZED';
  alter type ged.batch_status add value if not exists 'CANCELLED';
 end if;
end $$;

-- Índices são deliberadamente condicionais: instalações parcialmente migradas podem não ter a tabela/coluna.
do $$ begin
 if exists(select 1 from information_schema.columns where table_schema='ged' and table_name='label_print' and column_name='printed_at') then
  execute 'create index if not exists ix_label_print_tenant_printed on ged.label_print(tenant_id, printed_at desc)';
 end if;
 if exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_folder_move_history' and column_name='moved_at') then
  execute 'create index if not exists ix_dfmh_tenant_document_moved on ged.document_folder_move_history(tenant_id, document_id, moved_at desc)';
 end if;
 if exists(select 1 from information_schema.columns where table_schema='ged' and table_name='box_location_history' and column_name='changed_at') then
  execute 'create index if not exists ix_box_location_history_box on ged.box_location_history(tenant_id, box_id, changed_at desc)';
 end if;
 if exists(select 1 from information_schema.columns where table_schema='ged' and table_name='box_content_history' and column_name='changed_at') then
  execute 'create index if not exists ix_box_content_history_box on ged.box_content_history(tenant_id, box_id, changed_at desc)';
 end if;
 if exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_classification_audit' and column_name='created_at') then
  execute 'create index if not exists ix_document_classification_audit_subject on ged.document_classification_audit(tenant_id, document_id, created_at desc)';
 end if;
end $$;

-- Mantém o trigger específico; a função não faz cast nem enumera status, logo aceita valores legados e novos.
create or replace function ged.trg_batch_status_history() returns trigger language plpgsql as $$
begin
 if new.status is distinct from old.status then
  insert into ged.batch_history(tenant_id,batch_id,from_status,to_status,changed_at,event_time,event_type,changed_by,notes,data)
  values(new.tenant_id,new.id,old.status,new.status,now(),now(),'STATUS_CHANGED',
         coalesce((to_jsonb(new)->>'updated_by')::uuid,(to_jsonb(new)->>'created_by')::uuid),
         to_jsonb(new)->>'notes',jsonb_build_object('batch_no',to_jsonb(new)->>'batch_no'));
 end if;
 return new;
end $$;

do $$ begin
 if to_regclass('ged.batch') is not null then
  drop trigger if exists tr_batch_status_history on ged.batch;
  create trigger tr_batch_status_history after update of status on ged.batch
   for each row execute function ged.trg_batch_status_history();
 end if;
end $$;
