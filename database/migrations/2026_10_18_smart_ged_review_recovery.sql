begin;

alter table if exists ged.document_classification_suggestion
  add column if not exists recovered_at timestamptz,
  add column if not exists recovery_notes text;

alter table if exists ged.document_retention_suggestion
  add column if not exists recovered_at timestamptz,
  add column if not exists recovery_notes text;

create index if not exists ix_doc_class_suggestion_processing_recovery
  on ged.document_classification_suggestion(tenant_id, processing_expires_at, review_operation_id)
  where status = 'PROCESSING' and processing_expires_at is not null and reg_status = 'A';

create index if not exists ix_doc_ret_suggestion_processing_recovery
  on ged.document_retention_suggestion(tenant_id, processing_expires_at, review_operation_id)
  where status = 'PROCESSING' and processing_expires_at is not null and reg_status = 'A';

commit;
