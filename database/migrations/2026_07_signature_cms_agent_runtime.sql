-- Evolução 04.1 - Signing Agent CMS destacado. Idempotente e não destrutiva.
CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.signing_session (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid,
    content_hash text NOT NULL,
    content_hash_algorithm text NOT NULL DEFAULT 'SHA-256',
    nonce_hash text NOT NULL,
    content_download_token_hash text,
    expires_at timestamptz NOT NULL,
    completed_at timestamptz,
    cancelled_at timestamptz,
    failed_attempts integer NOT NULL DEFAULT 0,
    certificate_thumbprint text,
    signature_format text NOT NULL DEFAULT 'CMS_DETACHED',
    request_ip text,
    request_user_agent text,
    used_at timestamptz,
    status text NOT NULL DEFAULT 'REQUESTED',
    reg_status char(1) NOT NULL DEFAULT 'A',
    correlation_id text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS content_download_token_hash text;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS completed_at timestamptz;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS cancelled_at timestamptz;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS failed_attempts integer NOT NULL DEFAULT 0;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS certificate_thumbprint text;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS signature_format text NOT NULL DEFAULT 'CMS_DETACHED';
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS request_ip text;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS request_user_agent text;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS used_at timestamptz;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS document_version_id uuid;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_bytes bytea;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_format text NOT NULL DEFAULT 'INTERNAL';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_type text NOT NULL DEFAULT 'INTERNAL_LEGACY';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_profile text NOT NULL DEFAULT 'UNKNOWN';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS conformity_status text NOT NULL DEFAULT 'NOT_EVALUATED';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS content_hash text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS content_hash_algorithm text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_hash text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_der bytea;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_thumbprint text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_serial text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_not_before timestamptz;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_not_after timestamptz;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_subject_masked text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_issuer text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS cryptographic_status text NOT NULL DEFAULT 'INDETERMINATE';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS validation_status text NOT NULL DEFAULT 'INDETERMINATE';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS validated_at timestamptz;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS validation_engine_version text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS correlation_id text;

CREATE TABLE IF NOT EXISTS ged.signature_validation_check (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    signature_id uuid NOT NULL,
    signing_session_id uuid,
    check_name text NOT NULL,
    check_status text NOT NULL,
    message text NOT NULL,
    evidence_hash text,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_signing_session_tenant_status ON ged.signing_session(tenant_id, status, expires_at);
CREATE INDEX IF NOT EXISTS ix_signing_session_tenant_version ON ged.signing_session(tenant_id, document_version_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_signing_session_content_token ON ged.signing_session(tenant_id, content_download_token_hash) WHERE content_download_token_hash IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_signing_session_nonce_active ON ged.signing_session(tenant_id, nonce_hash) WHERE reg_status = 'A';
CREATE INDEX IF NOT EXISTS ix_document_signature_tenant_version ON ged.document_signature(tenant_id, document_version_id);
CREATE INDEX IF NOT EXISTS ix_document_signature_tenant_document_version ON ged.document_signature(tenant_id, document_id, document_version_id);
CREATE INDEX IF NOT EXISTS ix_document_signature_thumbprint ON ged.document_signature(tenant_id, certificate_thumbprint);
CREATE INDEX IF NOT EXISTS ix_signature_validation_check_signature ON ged.signature_validation_check(tenant_id, signature_id);

-- Evolução 04.1 runtime complement: tokens de uso único, idempotência, replay e evidências.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='signing_session' AND column_name='content_download_token_hash')
       AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='signing_session' AND column_name='content_token_hash') THEN
        ALTER TABLE ged.signing_session RENAME COLUMN content_download_token_hash TO content_token_hash;
    END IF;
END $$;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS content_token_hash text;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS signature_type text NOT NULL DEFAULT 'CMS_DETACHED';
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS completion_token_hash text;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS completion_idempotency_key text;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS completion_payload_hash text;
ALTER TABLE IF EXISTS ged.signing_session ADD COLUMN IF NOT EXISTS content_accessed_at timestamptz;

ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signing_session_id uuid;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_source text NOT NULL DEFAULT 'INTERNAL';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_format text NOT NULL DEFAULT 'INTERNAL';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_profile text NOT NULL DEFAULT 'UNKNOWN';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS conformity_status text NOT NULL DEFAULT 'NOT_EVALUATED';

CREATE TABLE IF NOT EXISTS ged.signature_validation_run (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    signature_id uuid NOT NULL,
    signing_session_id uuid,
    engine_version text NOT NULL,
    cryptographic_status text NOT NULL,
    validation_status text NOT NULL,
    conformity_status text NOT NULL DEFAULT 'NOT_EVALUATED',
    correlation_id text NOT NULL,
    validated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ged.signature_certificate_chain (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    signature_id uuid NOT NULL,
    certificate_der bytea NOT NULL,
    certificate_thumbprint text,
    chain_order integer NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ged.signature_evidence (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    signature_id uuid NOT NULL,
    evidence_type text NOT NULL,
    hash_algorithm text NOT NULL DEFAULT 'SHA256',
    evidence_hash text NOT NULL,
    evidence_bytes bytea,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    correlation_id text NOT NULL,
    captured_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_signing_session_completion ON ged.document_signature(tenant_id, signing_session_id) WHERE signing_session_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_signing_session_completion_token ON ged.signing_session(tenant_id, completion_token_hash) WHERE completion_token_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_signing_session_tenant_session ON ged.signing_session(tenant_id, id);
CREATE INDEX IF NOT EXISTS ix_signing_session_thumbprint ON ged.signing_session(tenant_id, certificate_thumbprint);
CREATE INDEX IF NOT EXISTS ix_document_signature_status ON ged.document_signature(tenant_id, cryptographic_status, validation_status, conformity_status);
CREATE INDEX IF NOT EXISTS ix_signature_validation_run_signature ON ged.signature_validation_run(tenant_id, signature_id);
CREATE INDEX IF NOT EXISTS ix_signature_certificate_chain_signature ON ged.signature_certificate_chain(tenant_id, signature_id);
CREATE INDEX IF NOT EXISTS ix_signature_evidence_signature ON ged.signature_evidence(tenant_id, signature_id);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_document_signature_cms_runtime_status') THEN
        ALTER TABLE ged.document_signature ADD CONSTRAINT ck_document_signature_cms_runtime_status
        CHECK (cryptographic_status IN ('PENDING','VALID','INVALID','DOCUMENT_CHANGED','SIGNATURE_CORRUPTED','UNSUPPORTED_ALGORITHM','INDETERMINATE','NOT_CRYPTOGRAPHICALLY_VERIFIED')
           AND conformity_status IN ('NOT_EVALUATED','INDETERMINATE','NON_COMPLIANT','COMPLIANT'));
    END IF;
END $$;
