-- Evolução 04.1.3 - CMS destacado end-to-end.
-- Migration aditiva, idempotente e compatível com execuções parciais anteriores.
CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS ged.signing_session (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NOT NULL,
    status text NOT NULL DEFAULT 'REQUESTED',
    content_hash text NOT NULL DEFAULT '',
    content_hash_algorithm text NOT NULL DEFAULT 'SHA-256',
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS content_download_token_hash text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS content_token_hash text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS completion_token_hash text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS nonce_hash text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS idempotency_key text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS completion_payload_hash text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS size_bytes bigint NOT NULL DEFAULT 0;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS file_name text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS document_code text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS version_label text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS first_content_accessed_at timestamptz NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS cancelled_at timestamptz NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS failed_at timestamptz NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS failure_count integer NOT NULL DEFAULT 0;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS safe_error text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS signature_id uuid NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS signature_type text NOT NULL DEFAULT 'CMS_DETACHED';
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS signature_format text NOT NULL DEFAULT 'CMS_PKCS7_DETACHED';
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS correlation_id text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS created_ip_hash text NULL;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS user_agent_hash text NULL;

UPDATE ged.signing_session
SET content_token_hash = COALESCE(NULLIF(content_token_hash, ''), NULLIF(content_download_token_hash, ''))
WHERE (content_token_hash IS NULL OR content_token_hash = '')
  AND content_download_token_hash IS NOT NULL;

CREATE TABLE IF NOT EXISTS ged.document_signature (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    signing_session_id uuid NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NOT NULL,
    signature_type text NOT NULL DEFAULT 'CMS_DETACHED',
    signature_format text NOT NULL DEFAULT 'CMS_PKCS7_DETACHED',
    signature_profile text NOT NULL DEFAULT 'UNKNOWN',
    signature_source text NOT NULL DEFAULT 'LOCAL_AGENT',
    cms_bytes bytea NULL,
    cms_sha256 text NULL,
    content_sha256 text NULL,
    certificate_der bytea NULL,
    certificate_chain_der bytea[] NULL,
    certificate_thumbprint text NULL,
    certificate_serial text NULL,
    signer_common_name text NULL,
    signer_cpf_masked text NULL,
    signer_cpf_hmac text NULL,
    signer_hmac_key_version text NULL,
    cryptographic_status text NOT NULL DEFAULT 'PENDING',
    validation_status text NOT NULL DEFAULT 'PENDING',
    conformity_status text NOT NULL DEFAULT 'NOT_EVALUATED',
    engine_version text NULL,
    correlation_id text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS signing_session_id uuid NULL;
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS cms_bytes bytea NULL;
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS cms_sha256 text NULL;
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS content_sha256 text NULL;
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS certificate_der bytea NULL;
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS certificate_chain_der bytea[] NULL;
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS signature_profile text NOT NULL DEFAULT 'UNKNOWN';
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS signature_source text NOT NULL DEFAULT 'LOCAL_AGENT';
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS cryptographic_status text NOT NULL DEFAULT 'PENDING';
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS validation_status text NOT NULL DEFAULT 'PENDING';
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS conformity_status text NOT NULL DEFAULT 'NOT_EVALUATED';
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS engine_version text NULL;
ALTER TABLE ged.document_signature ADD COLUMN IF NOT EXISTS correlation_id text NULL;

CREATE TABLE IF NOT EXISTS ged.signature_validation_run (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    signature_id uuid NOT NULL,
    cryptographic_status text NOT NULL,
    validation_status text NOT NULL,
    conformity_status text NOT NULL DEFAULT 'NOT_EVALUATED',
    engine_version text NULL,
    validated_at timestamptz NOT NULL DEFAULT now(),
    correlation_id text NULL
);

CREATE TABLE IF NOT EXISTS ged.signature_validation_check (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    validation_run_id uuid NOT NULL,
    name text NOT NULL,
    status text NOT NULL,
    message text NULL,
    evidence_hash text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ged.signature_certificate_chain (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    validation_run_id uuid NOT NULL,
    chain_order integer NOT NULL,
    certificate_der bytea NOT NULL,
    certificate_sha256 text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ged.signature_evidence (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    signature_id uuid NOT NULL,
    evidence_type text NOT NULL,
    hash_algorithm text NOT NULL,
    evidence_hash text NOT NULL,
    evidence_bytes bytea NULL,
    captured_at timestamptz NOT NULL DEFAULT now(),
    correlation_id text NULL
);

CREATE TABLE IF NOT EXISTS ged.signature_event (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    signing_session_id uuid NULL,
    signature_id uuid NULL,
    event_type text NOT NULL,
    safe_message text NULL,
    correlation_id text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_signing_session_content_token_canonical ON ged.signing_session(tenant_id, content_token_hash) WHERE content_token_hash IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_signing_session_completion_token ON ged.signing_session(tenant_id, completion_token_hash) WHERE completion_token_hash IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_signing_session_completion_idempotency ON ged.signing_session(tenant_id, id, idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_document_signature_tenant_session ON ged.document_signature(tenant_id, signing_session_id) WHERE signing_session_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_signing_session_tenant_user_status ON ged.signing_session(tenant_id, user_id, status, expires_at);
CREATE INDEX IF NOT EXISTS ix_signing_session_tenant_document_version ON ged.signing_session(tenant_id, document_id, document_version_id);
CREATE INDEX IF NOT EXISTS ix_document_signature_tenant_document ON ged.document_signature(tenant_id, document_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_document_signature_tenant_version ON ged.document_signature(tenant_id, document_id, document_version_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_signature_validation_run_tenant_signature ON ged.signature_validation_run(tenant_id, signature_id, validated_at DESC);
CREATE INDEX IF NOT EXISTS ix_signature_event_tenant_session ON ged.signature_event(tenant_id, signing_session_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_signature_event_tenant_signature ON ged.signature_event(tenant_id, signature_id, created_at DESC);
