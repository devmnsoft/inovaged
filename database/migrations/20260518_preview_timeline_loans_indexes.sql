CREATE TYPE IF NOT EXISTS ged.preview_processing_status AS ENUM ('PENDING','PROCESSING','READY','ERROR');

CREATE TABLE IF NOT EXISTS ged.preview_status (
    tenant_id uuid NOT NULL,
    document_version_id uuid NOT NULL,
    status ged.preview_processing_status NOT NULL DEFAULT 'PENDING',
    preview_path text NULL,
    error_message text NULL,
    requested_at timestamptz NULL,
    finished_at timestamptz NULL,
    PRIMARY KEY (tenant_id, document_version_id)
);

CREATE TABLE IF NOT EXISTS ged.document_timeline (
    id bigserial PRIMARY KEY,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NULL,
    event_type varchar(64) NOT NULL,
    event_payload jsonb NULL,
    justification text NULL,
    created_by uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_document_tenant_folder_status ON ged.document (tenant_id, folder_id, status);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'ged'
          AND table_name = 'document_version'
          AND column_name = 'is_current'
    ) THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_version_document_current ON ged.document_version (document_id, is_current)';
    ELSIF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'ged'
          AND table_name = 'document'
          AND column_name = 'current_version_id'
    ) THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_current_version ON ged.document (current_version_id) WHERE current_version_id IS NOT NULL';
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_version_status ON ged.ocr_job (tenant_id, document_version_id, status);
CREATE INDEX IF NOT EXISTS ix_preview_status_tenant_status ON ged.preview_status (tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_timeline_tenant_document_created_at ON ged.document_timeline (tenant_id, document_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_document_workflow_tenant_document ON ged.document_workflow (tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_workflow_history_tenant_document ON ged.document_workflow_history (tenant_id, document_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_loan_tenant_status_requested ON ged.loan (tenant_id, status, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_loan_tenant_approver_status ON ged.loan (tenant_id, approver_user_id, status);
