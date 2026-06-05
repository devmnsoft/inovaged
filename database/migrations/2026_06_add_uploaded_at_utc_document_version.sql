-- InovaGED: data/hora real de upload da versão documental.
-- Migration idempotente. A tabela usada pela listagem GED é ged.document_version.

BEGIN;

ALTER TABLE ged.document_version
  ADD COLUMN IF NOT EXISTS uploaded_at_utc timestamptz NULL;

UPDATE ged.document_version
SET uploaded_at_utc = COALESCE(uploaded_at_utc, created_at, now())
WHERE uploaded_at_utc IS NULL;

CREATE INDEX IF NOT EXISTS ix_document_version_uploaded_at_utc
  ON ged.document_version (uploaded_at_utc DESC);

COMMIT;
