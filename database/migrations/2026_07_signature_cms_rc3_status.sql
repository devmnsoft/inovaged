-- Evolução 04.1.9: persistência aditiva dos estados separados de certificado e confiança.
-- Migration idempotente e preservadora: não remove tabelas, colunas, documentos, versões ou assinaturas.

ALTER TABLE IF EXISTS ged.document_signature
    ADD COLUMN IF NOT EXISTS certificate_status text;

ALTER TABLE IF EXISTS ged.document_signature
    ADD COLUMN IF NOT EXISTS trust_status text;

ALTER TABLE IF EXISTS ged.signature_validation_run
    ADD COLUMN IF NOT EXISTS certificate_status text;

ALTER TABLE IF EXISTS ged.signature_validation_run
    ADD COLUMN IF NOT EXISTS trust_status text;

UPDATE ged.document_signature
SET certificate_status = COALESCE(NULLIF(certificate_status, ''), 'NOT_VERIFIABLE'),
    trust_status       = COALESCE(NULLIF(trust_status, ''), 'NOT_VERIFIABLE')
WHERE certificate_status IS NULL OR certificate_status = '' OR trust_status IS NULL OR trust_status = '';

UPDATE ged.signature_validation_run
SET certificate_status = COALESCE(NULLIF(certificate_status, ''), 'NOT_VERIFIABLE'),
    trust_status       = COALESCE(NULLIF(trust_status, ''), 'NOT_VERIFIABLE')
WHERE certificate_status IS NULL OR certificate_status = '' OR trust_status IS NULL OR trust_status = '';

ALTER TABLE IF EXISTS ged.document_signature
    ALTER COLUMN certificate_status SET DEFAULT 'NOT_VERIFIABLE';

ALTER TABLE IF EXISTS ged.document_signature
    ALTER COLUMN trust_status SET DEFAULT 'NOT_VERIFIABLE';

ALTER TABLE IF EXISTS ged.signature_validation_run
    ALTER COLUMN certificate_status SET DEFAULT 'NOT_VERIFIABLE';

ALTER TABLE IF EXISTS ged.signature_validation_run
    ALTER COLUMN trust_status SET DEFAULT 'NOT_VERIFIABLE';

CREATE INDEX IF NOT EXISTS ix_document_signature_cms_rc3_status
    ON ged.document_signature(tenant_id, certificate_status, trust_status, validation_status);

CREATE INDEX IF NOT EXISTS ix_signature_validation_run_cms_rc3_status
    ON ged.signature_validation_run(tenant_id, certificate_status, trust_status, validation_status);
