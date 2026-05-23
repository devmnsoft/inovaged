-- GED Dashboard support (idempotent)
CREATE INDEX IF NOT EXISTS ix_ocr_job_tenant_status_requested_at
ON ged.ocr_job (tenant_id, status, requested_at DESC);

CREATE INDEX IF NOT EXISTS ix_solicitacoes_tenant_status_data_solicitacao
ON ged.solicitacoes (tenant_id, status, data_solicitacao DESC);

CREATE INDEX IF NOT EXISTS ix_doc_folder_move_hist_tenant_moved_at
ON ged.document_folder_move_history (tenant_id, moved_at DESC);

CREATE INDEX IF NOT EXISTS ix_audit_log_tenant_event_time
ON ged.audit_log (tenant_id, event_time DESC);

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_enum e
    JOIN pg_type t ON t.oid = e.enumtypid
    JOIN pg_namespace n ON n.oid = t.typnamespace
    WHERE n.nspname='ged' AND t.typname='audit_action_enum' AND e.enumlabel='VIEW_GED_DASHBOARD'
  ) THEN
    ALTER TYPE ged.audit_action_enum ADD VALUE 'VIEW_GED_DASHBOARD';
  END IF;
END $$;
