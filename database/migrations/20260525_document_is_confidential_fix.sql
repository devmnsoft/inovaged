DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'ged'
          AND table_name = 'document'
          AND column_name = 'is_confidential'
    ) THEN
        -- coluna padrão já existe
        NULL;
    ELSIF EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema = 'ged' AND table_name = 'document' AND column_name = 'confidential'
    ) THEN
        EXECUTE 'ALTER TABLE ged.document RENAME COLUMN confidential TO is_confidential';
    ELSIF EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema = 'ged' AND table_name = 'document' AND column_name = 'sigilo'
    ) THEN
        EXECUTE 'ALTER TABLE ged.document RENAME COLUMN sigilo TO is_confidential';
    ELSIF EXISTS (
        SELECT 1 FROM information_schema.columns WHERE table_schema = 'ged' AND table_name = 'document' AND column_name = 'is_secret'
    ) THEN
        EXECUTE 'ALTER TABLE ged.document RENAME COLUMN is_secret TO is_confidential';
    ELSE
        ALTER TABLE ged.document ADD COLUMN is_confidential boolean;
    END IF;
END $$;

ALTER TABLE ged.document
    ALTER COLUMN is_confidential SET DEFAULT false;

UPDATE ged.document
SET is_confidential = false
WHERE is_confidential IS NULL;

ALTER TABLE ged.document
    ALTER COLUMN is_confidential SET NOT NULL;

CREATE INDEX IF NOT EXISTS ix_document_tenant_confidential
    ON ged.document (tenant_id, is_confidential)
    WHERE reg_status = 'A';
