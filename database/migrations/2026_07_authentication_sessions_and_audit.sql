BEGIN;

CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.authentication_session
(
    id                          uuid PRIMARY KEY,
    tenant_id                   uuid NOT NULL,
    user_id                     uuid NOT NULL,
    created_at_utc              timestamptz NOT NULL DEFAULT now(),
    last_activity_at_utc        timestamptz NOT NULL DEFAULT now(),
    expires_at_utc              timestamptz NOT NULL,
    absolute_expires_at_utc     timestamptz NOT NULL,
    revoked_at_utc              timestamptz NULL,
    revoked_by                  uuid NULL,
    revocation_reason           text NULL,
    ip_hash                     text NULL,
    user_agent_hash             text NULL,
    authentication_method       text NOT NULL DEFAULT 'PASSWORD',
    mfa_completed               boolean NOT NULL DEFAULT false,
    certificate_thumbprint_hash text NULL,
    security_stamp              text NOT NULL,
    correlation_id              text NULL,
    status                      text NOT NULL DEFAULT 'ACTIVE',
    CONSTRAINT ck_authentication_session_status
        CHECK (status IN ('ACTIVE', 'EXPIRED', 'REVOKED', 'REPLACED')),
    CONSTRAINT ck_authentication_session_expiration
        CHECK (expires_at_utc <= absolute_expires_at_utc)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_authentication_session_id
    ON ged.authentication_session (id);
CREATE INDEX IF NOT EXISTS ix_authentication_session_tenant_user
    ON ged.authentication_session (tenant_id, user_id);
CREATE INDEX IF NOT EXISTS ix_authentication_session_tenant_status
    ON ged.authentication_session (tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_authentication_session_expires
    ON ged.authentication_session (expires_at_utc);

ALTER TABLE IF EXISTS ged.app_audit_log
    ADD COLUMN IF NOT EXISTS correlation_id text NULL;

COMMIT;
