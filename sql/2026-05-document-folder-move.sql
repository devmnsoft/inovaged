CREATE TABLE IF NOT EXISTS ged.document_folder_move_history (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NULL,
    old_folder_id uuid NULL,
    new_folder_id uuid NOT NULL,
    moved_by uuid NOT NULL,
    moved_by_name text NULL,
    moved_at timestamp NOT NULL DEFAULT now(),
    reason text NULL,
    batch_id uuid NULL,
    source text NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);

CREATE INDEX IF NOT EXISTS ix_doc_move_hist_tenant_doc_movedat ON ged.document_folder_move_history (tenant_id, document_id, moved_at DESC);
CREATE INDEX IF NOT EXISTS ix_doc_move_hist_tenant_old_folder ON ged.document_folder_move_history (tenant_id, old_folder_id);
CREATE INDEX IF NOT EXISTS ix_doc_move_hist_tenant_new_folder ON ged.document_folder_move_history (tenant_id, new_folder_id);
CREATE INDEX IF NOT EXISTS ix_doc_move_hist_batch ON ged.document_folder_move_history (batch_id);

ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS updated_at timestamp;
ALTER TABLE ged.document ADD COLUMN IF NOT EXISTS updated_by uuid;

CREATE INDEX IF NOT EXISTS ix_document_tenant_folder ON ged.document (tenant_id, folder_id);
CREATE INDEX IF NOT EXISTS ix_document_tenant_reg_status ON ged.document (tenant_id, reg_status);
