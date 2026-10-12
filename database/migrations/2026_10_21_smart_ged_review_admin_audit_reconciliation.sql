-- Auditoria reconciliável para resoluções administrativas do Smart GED.
-- Não altera decisões existentes; apenas diferencia decisão gravada de auditoria efetivamente registrada.

alter table if exists ged.document_classification_suggestion
  add column if not exists admin_resolution_audit_status varchar(24) not null default 'NOT_REQUIRED',
  add column if not exists admin_resolution_audit_event text,
  add column if not exists admin_resolution_audit_recorded_at timestamptz,
  add column if not exists admin_resolution_audit_error text;

alter table if exists ged.document_retention_suggestion
  add column if not exists admin_resolution_audit_status varchar(24) not null default 'NOT_REQUIRED',
  add column if not exists admin_resolution_audit_event text,
  add column if not exists admin_resolution_audit_recorded_at timestamptz,
  add column if not exists admin_resolution_audit_error text;

update ged.document_classification_suggestion
set admin_resolution_audit_status = case
    when status = 'ADMIN_RESOLVED' then 'PENDING'
    else admin_resolution_audit_status
end
where status = 'ADMIN_RESOLVED'
  and admin_resolution_audit_status = 'NOT_REQUIRED'
  and coalesce(reg_status,'A') = 'A';

update ged.document_retention_suggestion
set admin_resolution_audit_status = case
    when status = 'ADMIN_RESOLVED' then 'PENDING'
    else admin_resolution_audit_status
end
where status = 'ADMIN_RESOLVED'
  and admin_resolution_audit_status = 'NOT_REQUIRED'
  and coalesce(reg_status,'A') = 'A';

create index if not exists ix_doc_class_suggestion_admin_audit_pending
  on ged.document_classification_suggestion(tenant_id, admin_resolution_audit_status, admin_resolution_at)
  where status = 'ADMIN_RESOLVED' and admin_resolution_audit_status in ('PENDING','FAILED') and reg_status='A';

create index if not exists ix_doc_ret_suggestion_admin_audit_pending
  on ged.document_retention_suggestion(tenant_id, admin_resolution_audit_status, admin_resolution_at)
  where status = 'ADMIN_RESOLVED' and admin_resolution_audit_status in ('PENDING','FAILED') and reg_status='A';
