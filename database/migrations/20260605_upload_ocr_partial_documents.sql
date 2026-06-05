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
SET uploaded_at_utc = COALESCE(uploaded_at_utc, created_at, now())
WHERE uploaded_at_utc IS NULL;

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

COMMIT;
