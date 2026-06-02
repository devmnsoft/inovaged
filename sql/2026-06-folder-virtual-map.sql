CREATE TABLE IF NOT EXISTS ged.folder_virtual_map
(
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    virtual_folder_id uuid NOT NULL,
    real_folder_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_folder_virtual_map_active
ON ged.folder_virtual_map(tenant_id, virtual_folder_id)
WHERE reg_status='A';

CREATE INDEX IF NOT EXISTS ix_folder_virtual_map_real
ON ged.folder_virtual_map(tenant_id, real_folder_id)
WHERE reg_status='A';
