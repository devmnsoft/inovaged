-- Evolução 04: assinatura digital ICP-Brasil - migração aditiva/idempotente.
CREATE SCHEMA IF NOT EXISTS ged;

ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS document_version_id uuid;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_type text NOT NULL DEFAULT 'INTERNAL_LEGACY';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_format text NOT NULL DEFAULT 'INTERNAL';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_profile text NOT NULL DEFAULT 'NOT_APPLICABLE';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_source text NOT NULL DEFAULT 'NOT_ICP_BRASIL';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS cryptographic_status text NOT NULL DEFAULT 'NOT_CRYPTOGRAPHICALLY_VERIFIED';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS validation_status text NOT NULL DEFAULT 'INTERNAL_ONLY';
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS policy_oid text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS policy_version text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS policy_hash text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS content_hash text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS content_hash_algorithm text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signature_hash text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_thumbprint text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_serial text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_not_before timestamptz;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_not_after timestamptz;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_subject_masked text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_issuer text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS certificate_der bytea;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS signed_file_version_id uuid;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS timestamp_status text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS timestamp_time timestamptz;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS validation_run_id uuid;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS validated_at timestamptz;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS validation_engine_version text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS legacy_status text;
ALTER TABLE IF EXISTS ged.document_signature ADD COLUMN IF NOT EXISTS correlation_id text;

UPDATE ged.document_signature
SET legacy_status = COALESCE(legacy_status, status::text),
    signature_type = COALESCE(NULLIF(signature_type, ''), 'INTERNAL_LEGACY'),
    signature_source = COALESCE(NULLIF(signature_source, ''), 'NOT_ICP_BRASIL'),
    cryptographic_status = COALESCE(NULLIF(cryptographic_status, ''), 'NOT_CRYPTOGRAPHICALLY_VERIFIED'),
    validation_status = COALESCE(NULLIF(validation_status, ''), 'INTERNAL_ONLY'),
    signature_profile = COALESCE(NULLIF(signature_profile, ''), 'NOT_APPLICABLE')
WHERE signature_type IS NULL OR signature_type IN ('INTERNAL','INTERNAL_LEGACY') OR validation_status IS NULL;

CREATE TABLE IF NOT EXISTS ged.signing_session (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, user_id uuid NOT NULL, document_id uuid NOT NULL, document_version_id uuid, content_hash text NOT NULL, content_hash_algorithm text NOT NULL, signature_type text NOT NULL, policy_oid text, nonce_hash text NOT NULL, expires_at timestamptz NOT NULL, status text NOT NULL, correlation_id text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), used_at timestamptz);
CREATE TABLE IF NOT EXISTS ged.signing_job (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, user_id uuid NOT NULL, total_count integer NOT NULL DEFAULT 0, completed_count integer NOT NULL DEFAULT 0, valid_count integer NOT NULL DEFAULT 0, invalid_count integer NOT NULL DEFAULT 0, failed_count integer NOT NULL DEFAULT 0, cancelled_count integer NOT NULL DEFAULT 0, status text NOT NULL, correlation_id text NOT NULL, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signing_job_item (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, job_id uuid NOT NULL, document_id uuid NOT NULL, document_version_id uuid, content_hash text, status text NOT NULL, validation_status text, message text, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signature_validation_run (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, signature_id uuid NOT NULL, status text NOT NULL, profile text NOT NULL, engine_version text NOT NULL, policy_catalog_version text, validated_at timestamptz NOT NULL DEFAULT now(), correlation_id text NOT NULL);
CREATE TABLE IF NOT EXISTS ged.signature_validation_check (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, validation_run_id uuid NOT NULL, check_name text NOT NULL, status text NOT NULL, message text NOT NULL, evidence_hash text, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signature_certificate_chain (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, signature_id uuid NOT NULL, position integer NOT NULL, subject_masked text, issuer text, serial text, thumbprint text, not_before timestamptz, not_after timestamptz, certificate_der bytea, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signature_revocation_evidence (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, signature_id uuid NOT NULL, source text NOT NULL, result text NOT NULL, response_hash text, this_update timestamptz, next_update timestamptz, evidence_bytes bytea, revocation_reason text, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signature_timestamp (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, signature_id uuid NOT NULL, policy_oid text, timestamp_time timestamptz, status text NOT NULL, token_hash text, token_bytes bytea, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signature_policy (id uuid PRIMARY KEY, oid text NOT NULL, name text NOT NULL, format text NOT NULL, profile text NOT NULL, status text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(oid, format, profile));
CREATE TABLE IF NOT EXISTS ged.signature_policy_version (id uuid PRIMARY KEY, policy_id uuid NOT NULL, version text NOT NULL, source text, hash text, valid_from timestamptz, valid_to timestamptz, active_for_generation boolean NOT NULL DEFAULT false, accepted_for_verification boolean NOT NULL DEFAULT true, last_validated_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(policy_id, version));
CREATE TABLE IF NOT EXISTS ged.signature_evidence (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, signature_id uuid NOT NULL, evidence_type text NOT NULL, hash_algorithm text NOT NULL, evidence_hash text NOT NULL, evidence_bytes bytea, correlation_id text NOT NULL, captured_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signature_event (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, signature_id uuid, signing_session_id uuid, job_id uuid, event_type text NOT NULL, message text NOT NULL, correlation_id text NOT NULL, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS ged.signature_import_detection (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, document_id uuid NOT NULL, document_version_id uuid, detected_format text NOT NULL, detection_status text NOT NULL, signature_count integer NOT NULL DEFAULT 0, validation_run_id uuid, created_at timestamptz NOT NULL DEFAULT now());

CREATE INDEX IF NOT EXISTS ix_document_signature_tenant_document_version ON ged.document_signature(tenant_id, document_id, document_version_id);
CREATE INDEX IF NOT EXISTS ix_document_signature_tenant_validation_status ON ged.document_signature(tenant_id, validation_status);
CREATE INDEX IF NOT EXISTS ix_document_signature_certificate_thumbprint ON ged.document_signature(tenant_id, certificate_thumbprint);
CREATE INDEX IF NOT EXISTS ix_signing_session_tenant_status ON ged.signing_session(tenant_id, status, expires_at);
CREATE INDEX IF NOT EXISTS ix_signing_job_tenant_status ON ged.signing_job(tenant_id, status, created_at);
CREATE INDEX IF NOT EXISTS ix_signature_validation_run_signature ON ged.signature_validation_run(tenant_id, signature_id, validated_at DESC);
