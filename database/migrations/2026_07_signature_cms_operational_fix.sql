-- Evolução 04.1.4 - reconciliação operacional CMS destacada.
-- Idempotente e não destrutiva: não executa DROP TABLE/COLUMN e preserva dados legados.
CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS ged.signing_session (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NOT NULL,
    status text NOT NULL,
    content_hash text NOT NULL,
    content_hash_algorithm text NOT NULL DEFAULT 'SHA-256',
    expires_at timestamptz NOT NULL,
    correlation_id text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS content_download_token_hash text;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS content_token_hash text;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS completion_token_hash text;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS nonce_hash text;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS idempotency_key text;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS completion_idempotency_key text;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS idempotency_payload_hash text;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS failure_count integer NOT NULL DEFAULT 0;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS failed_attempts integer NOT NULL DEFAULT 0;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS first_content_accessed_at timestamptz;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS content_token_consumed_at timestamptz;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS completion_token_consumed_at timestamptz;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS completed_at timestamptz;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS cancelled_at timestamptz;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS signature_id uuid;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS signature_type text NOT NULL DEFAULT 'CMS_DETACHED';
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS signature_format text NOT NULL DEFAULT 'CMS_PKCS7_DETACHED';
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS size_bytes bigint NOT NULL DEFAULT 0;
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS file_name text NOT NULL DEFAULT '';
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS document_code text NOT NULL DEFAULT '';
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS version_label text NOT NULL DEFAULT '';
ALTER TABLE ged.signing_session ADD COLUMN IF NOT EXISTS safe_error text;
UPDATE ged.signing_session SET content_token_hash = COALESCE(content_token_hash, content_download_token_hash), content_download_token_hash = COALESCE(content_download_token_hash, content_token_hash), completion_idempotency_key = COALESCE(completion_idempotency_key, idempotency_key), failure_count = GREATEST(COALESCE(failure_count,0), COALESCE(failed_attempts,0)), failed_attempts = GREATEST(COALESCE(failure_count,0), COALESCE(failed_attempts,0));
CREATE INDEX IF NOT EXISTS ix_signing_session_tenant_status ON ged.signing_session(tenant_id,status,expires_at);
CREATE INDEX IF NOT EXISTS ix_signing_session_completion_idempotency ON ged.signing_session(tenant_id,id,completion_idempotency_key,idempotency_payload_hash);

CREATE TABLE IF NOT EXISTS ged.document_signature (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    signing_session_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NOT NULL,
    signature_type text NOT NULL,
    signature_format text NOT NULL,
    signature_profile text NOT NULL,
    signature_source text NOT NULL,
    cryptographic_status text NOT NULL,
    validation_status text NOT NULL,
    conformity_status text NOT NULL DEFAULT 'NOT_EVALUATED',
    cms_bytes bytea NOT NULL,
    cms_sha256 text NOT NULL,
    content_sha256 text NOT NULL,
    certificate_der bytea NOT NULL,
    certificate_chain_der bytea[],
    engine_version text NOT NULL,
    correlation_id text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_document_signature_session ON ged.document_signature(tenant_id, signing_session_id);

CREATE TABLE IF NOT EXISTS ged.signature_validation_run (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    signature_id uuid NOT NULL,
    cryptographic_status text NOT NULL,
    validation_status text NOT NULL,
    conformity_status text NOT NULL DEFAULT 'NOT_EVALUATED',
    engine_version text NOT NULL,
    validated_at timestamptz NOT NULL DEFAULT now(),
    correlation_id text NOT NULL
);
CREATE TABLE IF NOT EXISTS ged.signature_validation_check (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid, validation_run_id uuid, message text, created_at timestamptz DEFAULT now());
ALTER TABLE ged.signature_validation_check ADD COLUMN IF NOT EXISTS name text;
ALTER TABLE ged.signature_validation_check ADD COLUMN IF NOT EXISTS status text;
ALTER TABLE ged.signature_validation_check ADD COLUMN IF NOT EXISTS check_name text;
ALTER TABLE ged.signature_validation_check ADD COLUMN IF NOT EXISTS check_status text;
ALTER TABLE ged.signature_validation_check ADD COLUMN IF NOT EXISTS evidence_hash text;
ALTER TABLE ged.signature_validation_check ADD COLUMN IF NOT EXISTS check_order integer NOT NULL DEFAULT 0;
UPDATE ged.signature_validation_check SET name=COALESCE(name,check_name), status=COALESCE(status,check_status);

CREATE TABLE IF NOT EXISTS ged.signature_certificate_chain (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid, certificate_der bytea, created_at timestamptz DEFAULT now());
ALTER TABLE ged.signature_certificate_chain ADD COLUMN IF NOT EXISTS signature_id uuid;
ALTER TABLE ged.signature_certificate_chain ADD COLUMN IF NOT EXISTS validation_run_id uuid;
ALTER TABLE ged.signature_certificate_chain ADD COLUMN IF NOT EXISTS chain_order integer NOT NULL DEFAULT 0;
ALTER TABLE ged.signature_certificate_chain ADD COLUMN IF NOT EXISTS certificate_sha256 text;
UPDATE ged.signature_certificate_chain SET certificate_sha256=lower(encode(digest(certificate_der,'sha256'),'hex')) WHERE certificate_der IS NOT NULL AND certificate_sha256 IS NULL;
UPDATE ged.signature_certificate_chain c SET signature_id = r.signature_id FROM ged.signature_validation_run r WHERE c.validation_run_id=r.id AND c.signature_id IS NULL;

CREATE TABLE IF NOT EXISTS ged.signature_event (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, signing_session_id uuid, signature_id uuid, event_type text NOT NULL, safe_message text NOT NULL, correlation_id text NOT NULL, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signature_evidence (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, signature_id uuid NOT NULL, evidence_type text NOT NULL, hash_algorithm text NOT NULL, evidence_hash text NOT NULL, evidence_bytes bytea, captured_at timestamptz NOT NULL, correlation_id text NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS ux_signature_evidence_hash ON ged.signature_evidence(tenant_id, signature_id, evidence_type, evidence_hash);
