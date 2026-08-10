-- Estabilização final aditiva para bancos limpos, legados e parcialmente migrados.
create schema if not exists ged;

-- O vínculo canônico fica no histórico de classificação; document.classification_id
-- permanece como fallback compatível com instalações legadas.
alter table if exists ged.document
  add column if not exists classification_id uuid;
alter table if exists ged.document_classification
  add column if not exists classification_id uuid;

-- Só executa quando a consolidação já criou ambas as tabelas. Isso permite incluir
-- esta hotfix antes das views e repeti-la ao final da cadeia de migrations.
do $$
begin
  if to_regclass('ged.document_classification') is not null
     and to_regclass('ged.document') is not null then
    update ged.document_classification dc
    set classification_id = d.classification_id
    from ged.document d
    where d.tenant_id = dc.tenant_id
      and d.id = dc.document_id
      and dc.classification_id is null
      and d.classification_id is not null;
  end if;
end $$;

-- Nomes de data/hora canônicos. ALTER TABLE IF EXISTS conserva instalações nas
-- quais um módulo opcional ainda não criou a tabela.
alter table if exists ged.document_folder_move_history add column if not exists moved_at timestamptz default now();
alter table if exists ged.box_location_history add column if not exists changed_at timestamptz default now();
alter table if exists ged.box_content_history add column if not exists changed_at timestamptz default now();
alter table if exists ged.label_print add column if not exists printed_at timestamptz default now();
alter table if exists ged.document_classification_audit add column if not exists created_at timestamptz default now();
alter table if exists ged.document_workflow_history add column if not exists performed_at timestamptz default now();

-- ged.batch.status nunca é convertido: somente o enum existente recebe valores.
do $$
begin
  if exists (
    select 1 from pg_type t
    join pg_namespace n on n.oid = t.typnamespace
    where n.nspname = 'ged' and t.typname = 'batch_status'
  ) then
    alter type ged.batch_status add value if not exists 'PREPARATION';
    alter type ged.batch_status add value if not exists 'CONFERENCE';
    alter type ged.batch_status add value if not exists 'ARCHIVING';
    alter type ged.batch_status add value if not exists 'FINALIZED';
    alter type ged.batch_status add value if not exists 'CANCELLED';
  end if;
end $$;
