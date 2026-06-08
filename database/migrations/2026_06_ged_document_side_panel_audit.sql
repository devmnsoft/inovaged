-- Eventos de auditoria do painel lateral operacional do GED.
DO $$
DECLARE
    enum_exists boolean;
    action_value text;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'ged'
          AND t.typname = 'audit_action_enum'
    ) INTO enum_exists;

    IF enum_exists THEN
        FOREACH action_value IN ARRAY ARRAY[
            'DOCUMENT_PANEL_VIEW',
            'FILE_PREVIEW',
            'OCR_VIEW',
            'DOCUMENT_HISTORY_VIEW'
        ] LOOP
            IF NOT EXISTS (
                SELECT 1
                FROM pg_enum e
                JOIN pg_type t ON t.oid = e.enumtypid
                JOIN pg_namespace n ON n.oid = t.typnamespace
                WHERE n.nspname = 'ged'
                  AND t.typname = 'audit_action_enum'
                  AND e.enumlabel = action_value
            ) THEN
                EXECUTE format('ALTER TYPE ged.audit_action_enum ADD VALUE %L', action_value);
            END IF;
        END LOOP;
    END IF;
END $$;
