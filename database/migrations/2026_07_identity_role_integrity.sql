-- Canonical identity relationship hardening. Idempotent and intentionally non-destructive.
DO $$
DECLARE
    invalid_links bigint;
BEGIN
    IF to_regclass('ged.app_user') IS NULL
       OR to_regclass('ged.app_role') IS NULL
       OR to_regclass('ged.user_role') IS NULL THEN
        RAISE EXCEPTION 'Identity migration requires ged.app_user, ged.app_role and ged.user_role';
    END IF;

    SELECT count(*) INTO invalid_links
    FROM ged.user_role ur
    LEFT JOIN ged.app_user u ON u.id = ur.user_id
    LEFT JOIN ged.app_role r ON r.id = ur.role_id
    WHERE u.id IS NULL OR r.id IS NULL OR u.tenant_id <> r.tenant_id;

    IF invalid_links > 0 THEN
        RAISE EXCEPTION
            'Identity migration stopped: % orphan or cross-tenant user_role link(s) require administrative correction',
            invalid_links;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_user_role_user ON ged.user_role(user_id);
CREATE INDEX IF NOT EXISTS ix_user_role_role ON ged.user_role(role_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_user_role_user_role ON ged.user_role(user_id, role_id);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ged.user_role'::regclass AND contype = 'f'
          AND confrelid = 'ged.app_user'::regclass
          AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'ged.user_role'::regclass AND attname = 'user_id')]::smallint[]
    ) THEN
        ALTER TABLE ged.user_role
            ADD CONSTRAINT fk_user_role_user FOREIGN KEY (user_id) REFERENCES ged.app_user(id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ged.user_role'::regclass AND contype = 'f'
          AND confrelid = 'ged.app_role'::regclass
          AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'ged.user_role'::regclass AND attname = 'role_id')]::smallint[]
    ) THEN
        ALTER TABLE ged.user_role
            ADD CONSTRAINT fk_user_role_role FOREIGN KEY (role_id) REFERENCES ged.app_role(id);
    END IF;
END $$;

CREATE OR REPLACE FUNCTION ged.enforce_user_role_same_tenant()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    user_tenant uuid;
    role_tenant uuid;
BEGIN
    SELECT tenant_id INTO user_tenant FROM ged.app_user WHERE id = NEW.user_id;
    IF user_tenant IS NULL THEN
        RAISE EXCEPTION 'CROSS_TENANT_ROLE_BLOCKED: user does not exist';
    END IF;

    SELECT tenant_id INTO role_tenant FROM ged.app_role WHERE id = NEW.role_id;
    IF role_tenant IS NULL THEN
        RAISE EXCEPTION 'CROSS_TENANT_ROLE_BLOCKED: role does not exist';
    END IF;

    IF user_tenant <> role_tenant THEN
        RAISE EXCEPTION 'CROSS_TENANT_ROLE_BLOCKED: user and role belong to different tenants';
    END IF;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_user_role_same_tenant ON ged.user_role;
CREATE TRIGGER trg_user_role_same_tenant
BEFORE INSERT OR UPDATE OF user_id, role_id ON ged.user_role
FOR EACH ROW EXECUTE FUNCTION ged.enforce_user_role_same_tenant();

CREATE OR REPLACE VIEW ged.vw_user_role_effective AS
SELECT
    u.tenant_id,
    ur.user_id,
    ur.role_id,
    r.name AS role_name,
    r.normalized_name
FROM ged.user_role ur
JOIN ged.app_user u ON u.id = ur.user_id
JOIN ged.app_role r ON r.id = ur.role_id AND r.tenant_id = u.tenant_id;
