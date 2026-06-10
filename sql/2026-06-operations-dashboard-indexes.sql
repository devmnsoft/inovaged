CREATE INDEX IF NOT EXISTS ix_operations_document_tenant_folder_status
    ON ged.document (tenant_id, folder_id, status, reg_status);

CREATE INDEX IF NOT EXISTS ix_operations_document_created_by
    ON ged.document (tenant_id, created_by, created_at DESC)
    WHERE reg_status = 'A';

CREATE INDEX IF NOT EXISTS ix_operations_document_version_uploaded
    ON ged.document_version (tenant_id, created_at DESC, document_id);

CREATE INDEX IF NOT EXISTS ix_operations_ocr_job_tenant_status_requested
    ON ged.ocr_job (tenant_id, status, requested_at DESC);

CREATE INDEX IF NOT EXISTS ix_operations_loan_request_tenant_status_due
    ON ged.loan_request (tenant_id, status, due_at, reg_status);

CREATE INDEX IF NOT EXISTS ix_operations_app_audit_log_tenant_created_action
    ON ged.app_audit_log (tenant_id, created_at DESC, action);

CREATE INDEX IF NOT EXISTS ix_operations_protocolo_tenant_status_created
    ON ged.protocolo (tenant_id, status, created_at DESC, reg_status);
