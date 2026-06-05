-- InovaGED: UTC upload timestamps, OCR availability support, and partial document metadata.
BEGIN;

ALTER TABLE ged.document_version
  ADD COLUMN IF NOT EXISTS uploaded_at_utc timestamptz NOT NULL DEFAULT now(),
  ADD COLUMN IF NOT EXISTS is_partial_document boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS is_document_incomplete boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS part_number integer NULL,
  ADD COLUMN IF NOT EXISTS total_parts integer NULL,
  ADD COLUMN IF NOT EXISTS consolidated_version_id uuid NULL;

UPDATE ged.document_version
SET uploaded_at_utc = COALESCE(uploaded_at_utc, created_at, now()),
    is_partial_document = COALESCE(is_partial_document, false),
    is_document_incomplete = COALESCE(is_document_incomplete, false)
WHERE uploaded_at_utc IS NULL
   OR is_partial_document IS NULL
   OR is_document_incomplete IS NULL;

ALTER TABLE ged.document_version
  ALTER COLUMN uploaded_at_utc SET DEFAULT now(),
  ALTER COLUMN uploaded_at_utc SET NOT NULL,
  ALTER COLUMN is_partial_document SET DEFAULT false,
  ALTER COLUMN is_partial_document SET NOT NULL,
  ALTER COLUMN is_document_incomplete SET DEFAULT false,
  ALTER COLUMN is_document_incomplete SET NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'fk_document_version_consolidated_version'
      AND conrelid = 'ged.document_version'::regclass
  ) THEN
    ALTER TABLE ged.document_version
      ADD CONSTRAINT fk_document_version_consolidated_version
      FOREIGN KEY (consolidated_version_id)
      REFERENCES ged.document_version(id);
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_document_version_uploaded_at_utc
  ON ged.document_version (tenant_id, uploaded_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_document_version_partial_document
  ON ged.document_version (tenant_id, document_id, is_partial_document, is_document_incomplete);

CREATE TABLE IF NOT EXISTS ged.document_part (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  document_id uuid NOT NULL,
  version_id uuid NOT NULL,
  part_number integer NULL,
  total_parts integer NULL,
  uploaded_at_utc timestamptz NOT NULL DEFAULT now(),
  is_consolidated boolean NOT NULL DEFAULT false,
  consolidated_at_utc timestamptz NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT fk_document_part_document
    FOREIGN KEY (document_id) REFERENCES ged.document(id) ON DELETE CASCADE,
  CONSTRAINT fk_document_part_version
    FOREIGN KEY (version_id) REFERENCES ged.document_version(id) ON DELETE CASCADE,
  CONSTRAINT uq_document_part_version UNIQUE (tenant_id, document_id, version_id)
);

CREATE INDEX IF NOT EXISTS ix_document_part_document
  ON ged.document_part (tenant_id, document_id, part_number);

COMMIT;
