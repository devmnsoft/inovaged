-- Recuperação idempotente da reserva de auditoria administrativa do Smart GED.
-- Aditiva: não altera decisões, apenas registra identidade estável e expiração da reserva RECORDING.

alter table if exists ged.document_classification_suggestion
  add column if not exists admin_resolution_audit_event_identity text,
  add column if not exists admin_resolution_audit_claimed_at timestamptz,
  add column if not exists admin_resolution_audit_claim_expires_at timestamptz;

alter table if exists ged.document_retention_suggestion
  add column if not exists admin_resolution_audit_event_identity text,
  add column if not exists admin_resolution_audit_claimed_at timestamptz,
  add column if not exists admin_resolution_audit_claim_expires_at timestamptz;

update ged.document_classification_suggestion
set admin_resolution_audit_event_identity = 'smart-ged-admin-resolution:' || replace(tenant_id::text, '-', '') || ':classification:' || replace(id::text, '-', '')
where status = 'ADMIN_RESOLVED'
  and admin_resolution_audit_event_identity is null
  and coalesce(reg_status,'A') = 'A';

update ged.document_retention_suggestion
set admin_resolution_audit_event_identity = 'smart-ged-admin-resolution:' || replace(tenant_id::text, '-', '') || ':retention:' || replace(id::text, '-', '')
where status = 'ADMIN_RESOLVED'
  and admin_resolution_audit_event_identity is null
  and coalesce(reg_status,'A') = 'A';

update ged.document_classification_suggestion
set admin_resolution_audit_status = 'FAILED',
    admin_resolution_audit_error = coalesce(admin_resolution_audit_error, 'Reserva de auditoria abandonada; reconciliar sem repetir a decisão.')
where status = 'ADMIN_RESOLVED'
  and admin_resolution_audit_status = 'RECORDING'
  and coalesce(admin_resolution_audit_claim_expires_at, admin_resolution_at) < now()
  and coalesce(reg_status,'A') = 'A';

update ged.document_retention_suggestion
set admin_resolution_audit_status = 'FAILED',
    admin_resolution_audit_error = coalesce(admin_resolution_audit_error, 'Reserva de auditoria abandonada; reconciliar sem repetir a decisão.')
where status = 'ADMIN_RESOLVED'
  and admin_resolution_audit_status = 'RECORDING'
  and coalesce(admin_resolution_audit_claim_expires_at, admin_resolution_at) < now()
  and coalesce(reg_status,'A') = 'A';

create index if not exists ix_doc_class_suggestion_admin_audit_claims
  on ged.document_classification_suggestion(tenant_id, admin_resolution_audit_status, admin_resolution_audit_claim_expires_at)
  where status = 'ADMIN_RESOLVED' and admin_resolution_audit_status in ('PENDING','FAILED','RECORDING') and reg_status='A';

create index if not exists ix_doc_ret_suggestion_admin_audit_claims
  on ged.document_retention_suggestion(tenant_id, admin_resolution_audit_status, admin_resolution_audit_claim_expires_at)
  where status = 'ADMIN_RESOLVED' and admin_resolution_audit_status in ('PENDING','FAILED','RECORDING') and reg_status='A';
