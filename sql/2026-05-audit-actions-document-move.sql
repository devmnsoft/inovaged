DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='ged' AND t.typname='audit_action_enum') THEN
        RAISE NOTICE 'Enum ged.audit_action_enum não encontrado';
        RETURN;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='ged' AND t.typname='audit_action_enum' AND e.enumlabel='MOVE_DOCUMENT_FOLDER') THEN
        ALTER TYPE ged.audit_action_enum ADD VALUE 'MOVE_DOCUMENT_FOLDER';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='ged' AND t.typname='audit_action_enum' AND e.enumlabel='MOVE_DOCUMENT_FOLDER_BULK') THEN
        ALTER TYPE ged.audit_action_enum ADD VALUE 'MOVE_DOCUMENT_FOLDER_BULK';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='ged' AND t.typname='audit_action_enum' AND e.enumlabel='ACCESS_DENIED_MOVE_DOCUMENT') THEN
        ALTER TYPE ged.audit_action_enum ADD VALUE 'ACCESS_DENIED_MOVE_DOCUMENT';
    END IF;
END $$;
