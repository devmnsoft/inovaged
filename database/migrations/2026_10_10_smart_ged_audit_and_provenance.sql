-- Migration 2026_10_10_smart_ged_audit_and_provenance.sql
-- Vinculação de análises e sugestões de IA à origem real (documentId, versionId, hash, plano, política, modelo)
-- e correção de schema para classificação e temporalidade.

-- 1. Colunas de linhagem em ged.document_ai_analysis
alter table if exists ged.document_ai_analysis
  add column if not exists version_id uuid,
  add column if not exists source_hash varchar(128),
  add column if not exists plan_version_id uuid,
  add column if not exists policy_name varchar(100),
  add column if not exists model_name varchar(100),
  add column if not exists is_legacy_unproven boolean not null default false;

-- 2. Colunas de linhagem em ged.document_classification_suggestion
alter table if exists ged.document_classification_suggestion
  add column if not exists version_id uuid,
  add column if not exists source_hash varchar(128),
  add column if not exists plan_version_id uuid,
  add column if not exists policy_name varchar(100),
  add column if not exists model_name varchar(100),
  add column if not exists is_legacy_unproven boolean not null default false;

-- 3. Colunas de linhagem em ged.document_retention_suggestion
alter table if exists ged.document_retention_suggestion
  add column if not exists version_id uuid,
  add column if not exists source_hash varchar(128),
  add column if not exists plan_version_id uuid,
  add column if not exists policy_name varchar(100),
  add column if not exists model_name varchar(100),
  add column if not exists is_legacy_unproven boolean not null default false;

-- 4. Marcar registros legados sem versão como não comprovados
update ged.document_ai_analysis
set is_legacy_unproven = true
where version_id is null;

update ged.document_classification_suggestion
set is_legacy_unproven = true
where version_id is null;

update ged.document_retention_suggestion
set is_legacy_unproven = true
where version_id is null;

-- 5. Garantir existência de colunas title e name em ged.classification_plan e ged.classification_plan_version_item
alter table if exists ged.classification_plan
  add column if not exists title text,
  add column if not exists name varchar(255);

update ged.classification_plan
set title = name
where title is null and name is not null;

update ged.classification_plan
set name = title
where name is null and title is not null;

alter table if exists ged.classification_plan_version_item
  add column if not exists title text,
  add column if not exists name varchar(255);

update ged.classification_plan_version_item
set title = name
where title is null and name is not null;

update ged.classification_plan_version_item
set name = title
where name is null and title is not null;

-- 6. Índices para recuperação eficiente por versão
create index if not exists ix_document_ai_analysis_version
  on ged.document_ai_analysis(tenant_id, document_id, version_id);

create index if not exists ix_doc_class_suggestion_version
  on ged.document_classification_suggestion(tenant_id, document_id, version_id);

create index if not exists ix_doc_ret_suggestion_version
  on ged.document_retention_suggestion(tenant_id, document_id, version_id);
