-- InovaGED - Suporte idempotente para movimentação segura de pastas GED.

alter table if exists ged.folder
add column if not exists updated_at timestamptz null;

alter table if exists ged.folder
add column if not exists updated_by uuid null;

create index if not exists ix_folder_tenant_parent_name
on ged.folder(tenant_id, parent_id, lower(name))
where coalesce(reg_status, 'A') = 'A';

DO $$
DECLARE
    enum_exists boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'ged'
          AND t.typname = 'audit_action_enum'
    ) INTO enum_exists;

    IF enum_exists AND NOT EXISTS (
        SELECT 1
        FROM pg_enum e
        JOIN pg_type t ON t.oid = e.enumtypid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'ged'
          AND t.typname = 'audit_action_enum'
          AND e.enumlabel = 'GED_FOLDER_MOVED'
    ) THEN
        ALTER TYPE ged.audit_action_enum ADD VALUE 'GED_FOLDER_MOVED';
    END IF;
END $$;
