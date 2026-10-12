-- Resolução administrativa de revisões assistidas com efeito aplicado pendente ou indeterminado.
-- Incremental: preserva decisões anteriores e não reaplica efeitos.

alter table if exists ged.document_classification_suggestion
  add column if not exists admin_resolution_by uuid,
  add column if not exists admin_resolution_at timestamptz,
  add column if not exists admin_resolution_reason text,
  add column if not exists admin_resolution_evidence text,
  add column if not exists admin_resolution_previous_status varchar(40);

alter table if exists ged.document_retention_suggestion
  add column if not exists admin_resolution_by uuid,
  add column if not exists admin_resolution_at timestamptz,
  add column if not exists admin_resolution_reason text,
  add column if not exists admin_resolution_evidence text,
  add column if not exists admin_resolution_previous_status varchar(40);

create index if not exists ix_doc_class_suggestion_admin_resolution
  on ged.document_classification_suggestion(tenant_id, status, admin_resolution_at)
  where status in ('FAILED_APPLIED','RECOVERY_INDETERMINATE','ADMIN_RESOLVED') and reg_status='A';

create index if not exists ix_doc_ret_suggestion_admin_resolution
  on ged.document_retention_suggestion(tenant_id, status, admin_resolution_at)
  where status in ('FAILED_APPLIED','RECOVERY_INDETERMINATE','ADMIN_RESOLVED') and reg_status='A';
