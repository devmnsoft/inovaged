-- Compatibilidade e robustez para SmartSearch/GED. Idempotente e textual.
CREATE SCHEMA IF NOT EXISTS ged;
DO $$ BEGIN CREATE EXTENSION IF NOT EXISTS unaccent; EXCEPTION WHEN insufficient_privilege THEN RAISE NOTICE 'Sem permissão para unaccent.'; WHEN others THEN RAISE NOTICE 'unaccent indisponível: %', SQLERRM; END $$;
DO $$ BEGIN CREATE EXTENSION IF NOT EXISTS pg_trgm; EXCEPTION WHEN insufficient_privilege THEN RAISE NOTICE 'Sem permissão para pg_trgm.'; WHEN others THEN RAISE NOTICE 'pg_trgm indisponível: %', SQLERRM; END $$;

CREATE TABLE IF NOT EXISTS ged.document_search_index (
    id uuid DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NULL,
    version_id uuid NULL,
    title text NULL,
    file_name text NULL,
    document_type text NULL,
    classification text NULL,
    classification_name text NULL,
    folder_id uuid NULL,
    folder_name text NULL,
    patient_name text NULL,
    medical_record_number text NULL,
    protocol_number text NULL,
    extracted_age int NULL,
    extracted_year int NULL,
    extracted_terms text[] NULL,
    ocr_text text NULL,
    search_text text NOT NULL DEFAULT '',
    search_vector tsvector NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    last_indexed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_document_search_index_tenant_document UNIQUE (tenant_id, document_id)
);

ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS search_text text NOT NULL DEFAULT '';
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS last_indexed_at timestamptz NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS classification_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS protocol_number text NULL;

UPDATE ged.document_search_index SET search_text = coalesce(search_text, '');
UPDATE ged.document_search_index SET document_version_id = version_id WHERE document_version_id IS NULL AND version_id IS NOT NULL;
UPDATE ged.document_search_index SET version_id = document_version_id WHERE version_id IS NULL AND document_version_id IS NOT NULL;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname='unaccent') THEN
        UPDATE ged.document_search_index SET search_vector = to_tsvector('portuguese', unaccent(coalesce(search_text,''))) WHERE search_vector IS NULL;
    ELSE
        UPDATE ged.document_search_index SET search_vector = to_tsvector('portuguese', coalesce(search_text,'')) WHERE search_vector IS NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_document ON ged.document_search_index(tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_folder ON ged.document_search_index(tenant_id, folder_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_vector ON ged.document_search_index USING GIN(search_vector);
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname='pg_trgm') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_document_search_index_text_trgm ON ged.document_search_index USING GIN(search_text gin_trgm_ops)';
    END IF;
END $$;
