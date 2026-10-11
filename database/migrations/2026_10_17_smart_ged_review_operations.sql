-- Operações persistidas de revisão assistida do Smart GED.
-- Incremental e idempotente: não altera decisões históricas nem reescreve migrations publicadas.

alter table if exists ged.document_classification_suggestion
  add column if not exists review_operation_id uuid,
  add column if not exists processing_started_at timestamptz,
  add column if not exists processing_expires_at timestamptz,
  add column if not exists applied_at timestamptz,
  add column if not exists failed_at timestamptz,
  add column if not exists failure_code varchar(80),
  add column if not exists public_failure_message text;

alter table if exists ged.document_retention_suggestion
  add column if not exists review_operation_id uuid,
  add column if not exists processing_started_at timestamptz,
  add column if not exists processing_expires_at timestamptz,
  add column if not exists applied_at timestamptz,
  add column if not exists failed_at timestamptz,
  add column if not exists failure_code varchar(80),
  add column if not exists public_failure_message text;

create index if not exists ix_doc_class_suggestion_processing_recovery
  on ged.document_classification_suggestion(tenant_id, status, processing_expires_at)
  where status in ('PROCESSING','FAILED_APPLIED') and reg_status='A';

create index if not exists ix_doc_ret_suggestion_processing_recovery
  on ged.document_retention_suggestion(tenant_id, status, processing_expires_at)
  where status in ('PROCESSING','FAILED_APPLIED') and reg_status='A';

create unique index if not exists ux_doc_class_suggestion_review_operation
  on ged.document_classification_suggestion(tenant_id, review_operation_id)
  where review_operation_id is not null;

create unique index if not exists ux_doc_ret_suggestion_review_operation
  on ged.document_retention_suggestion(tenant_id, review_operation_id)
  where review_operation_id is not null;
