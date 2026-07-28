\set ON_ERROR_STOP on
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='ged' AND indexname='ux_user_role_user_role') THEN
        RAISE EXCEPTION 'missing unique user_role index';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_user_role_same_tenant' AND NOT tgisinternal) THEN
        RAISE EXCEPTION 'missing user_role tenant trigger';
    END IF;
    IF to_regclass('ged.vw_user_role_effective') IS NULL THEN
        RAISE EXCEPTION 'missing effective role view';
    END IF;
END $$;

SELECT DISTINCT r.normalized_name
FROM ged.app_user u
JOIN ged.user_role ur ON ur.user_id = u.id
JOIN ged.app_role r ON r.id = ur.role_id AND r.tenant_id = u.tenant_id
WHERE u.tenant_id = '10000000-0000-0000-0000-000000000001'
  AND u.id = '20000000-0000-0000-0000-000000000001'
  AND u.is_active = true AND u.deleted_at_utc IS NULL;
