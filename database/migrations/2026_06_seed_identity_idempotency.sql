-- Idempotency guarantees for SystemSeed identity data.
-- The seed queries before inserting; these indexes enforce the same invariants at the database boundary.

DO $$
BEGIN
    IF to_regclass('ged.app_user') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM pg_indexes
           WHERE schemaname = 'ged'
             AND indexname = 'ux_app_user_tenant_email'
       ) THEN
        CREATE UNIQUE INDEX ux_app_user_tenant_email
            ON ged.app_user(tenant_id, lower(email));
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('ged.app_role') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM pg_indexes
           WHERE schemaname = 'ged'
             AND indexname = 'ux_app_role_tenant_name'
       ) THEN
        CREATE UNIQUE INDEX ux_app_role_tenant_name
            ON ged.app_role(tenant_id, normalized_name);
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('ged.user_role') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM pg_indexes
           WHERE schemaname = 'ged'
             AND indexname = 'ux_app_user_role_user_role'
       ) THEN
        CREATE UNIQUE INDEX ux_app_user_role_user_role
            ON ged.user_role(user_id, role_id);
    END IF;
END $$;
