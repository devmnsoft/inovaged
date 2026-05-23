-- Idempotent migration: soft delete + loans indexes
ALTER TABLE ged.loan_request
ADD COLUMN IF NOT EXISTS deleted_at timestamptz NULL,
ADD COLUMN IF NOT EXISTS deleted_by uuid NULL,
ADD COLUMN IF NOT EXISTS delete_reason text NULL;

CREATE INDEX IF NOT EXISTS ix_loan_request_tenant_status_requested
ON ged.loan_request(tenant_id, status, requested_at DESC)
WHERE reg_status = 'A';

CREATE INDEX IF NOT EXISTS ix_loan_request_tenant_requester
ON ged.loan_request(tenant_id, requester_id, requested_at DESC)
WHERE reg_status = 'A';

CREATE INDEX IF NOT EXISTS ix_loan_history_tenant_loan
ON ged.loan_history(tenant_id, loan_id, event_time DESC)
WHERE reg_status = 'A';
