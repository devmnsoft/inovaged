-- Empréstimos: itens manuais/físicos em solicitações.
-- Idempotente e seguro para tabelas com dados existentes.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.loan_request_item (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    loan_request_id uuid NULL,
    loan_id uuid NULL,
    document_id uuid NULL,
    is_physical boolean NOT NULL DEFAULT false,
    is_manual boolean NOT NULL DEFAULT false,
    reference_code text NULL,
    description text NULL,
    document_type text NULL,
    patient_name text NULL,
    medical_record_number text NULL,
    box_code text NULL,
    physical_location text NULL,
    notes text NULL,
    document_version_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);

ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS is_manual boolean NOT NULL DEFAULT false;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS is_physical boolean NOT NULL DEFAULT false;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS reference_code text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS description text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS document_type text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS patient_name text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS medical_record_number text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS box_code text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS physical_location text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS notes text NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS loan_request_id uuid NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS loan_id uuid NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS document_id uuid NULL;
ALTER TABLE ged.loan_request_item ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

UPDATE ged.loan_request_item
SET loan_request_id = COALESCE(loan_request_id, loan_id),
    loan_id = COALESCE(loan_id, loan_request_id),
    is_manual = COALESCE(is_manual, document_id IS NULL, false),
    created_at = COALESCE(created_at, now()),
    reg_status = COALESCE(reg_status, 'A')
WHERE loan_request_id IS NULL
   OR loan_id IS NULL
   OR is_manual IS NULL
   OR created_at IS NULL
   OR reg_status IS NULL;

CREATE INDEX IF NOT EXISTS ix_loan_request_item_request
ON ged.loan_request_item(loan_request_id);

CREATE INDEX IF NOT EXISTS ix_loan_request_item_document
ON ged.loan_request_item(document_id);

CREATE INDEX IF NOT EXISTS ix_loan_request_item_manual
ON ged.loan_request_item(is_manual);
