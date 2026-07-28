CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE ged.tenant (id uuid PRIMARY KEY, code text NOT NULL, is_active boolean NOT NULL);
CREATE TABLE ged.app_user (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES ged.tenant(id),
    email text,
    is_active boolean NOT NULL DEFAULT true,
    deleted_at_utc timestamptz
);
CREATE TABLE ged.app_role (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES ged.tenant(id),
    name text NOT NULL,
    normalized_name text NOT NULL
);
-- Deliberately reproduces the production relationship: no tenant_id or is_active.
CREATE TABLE ged.user_role (
    user_id uuid NOT NULL,
    role_id uuid NOT NULL,
    PRIMARY KEY(user_id, role_id)
);

INSERT INTO ged.tenant VALUES ('10000000-0000-0000-0000-000000000001', 'default', true);
INSERT INTO ged.app_user VALUES ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'legacy@example.invalid', true, NULL);
INSERT INTO ged.app_role VALUES ('30000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Operador', 'OPERADOR');
INSERT INTO ged.user_role VALUES ('20000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001');
