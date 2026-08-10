-- Compatibilidade definitiva entre a classificação legada do documento e o plano arquivístico.
-- Aditiva, idempotente e segura para bancos limpos, restaurados ou parcialmente migrados.
create schema if not exists ged;

alter table if exists ged.document_classification
  add column if not exists classification_id uuid;

-- A coluna provisória pode ou não existir. SQL dinâmico evita que o parser a resolva
-- em bancos legados onde ela nunca foi criada.
do $$
begin
  if to_regclass('ged.document_classification') is not null
     and exists (
       select 1
       from information_schema.columns
       where table_schema = 'ged'
         and table_name = 'document_classification'
         and column_name = 'classification_plan_id'
     ) then
    execute format(
      'update %I.%I set %I = %I where %I is null and %I is not null',
      'ged', 'document_classification',
      'classification_id', 'classification_plan_id',
      'classification_id', 'classification_plan_id'
    );
  end if;
end $$;

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

do $$
begin
  if to_regclass('ged.document_classification') is not null then
    execute 'create index if not exists ix_document_classification_tenant_classification '
            'on ged.document_classification(tenant_id, classification_id)';
  end if;
end $$;
