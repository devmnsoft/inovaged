-- InovaGED - GED Smart Search Intelligence (idempotent, text-only)
CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
DO $$ BEGIN
    CREATE EXTENSION IF NOT EXISTS unaccent;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'unaccent extension was not created due to insufficient privileges; smart search will use fallback semantics.';
END $$;
DO $$ BEGIN
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'pg_trgm extension was not created due to insufficient privileges; smart search will use ILIKE fallback semantics.';
END $$;

CREATE TABLE IF NOT EXISTS ged.search_synonym (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    term text NOT NULL,
    synonym text NOT NULL,
    category text NULL,
    weight numeric NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_search_synonym_tenant_term_synonym ON ged.search_synonym(tenant_id, lower(term), lower(synonym));

CREATE TABLE IF NOT EXISTS ged.document_search_index (
    id uuid DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NULL,
    version_id uuid NULL,
    folder_id uuid NULL,
    title text NULL,
    file_name text NULL,
    document_type text NULL,
    classification_name text NULL,
    classification text NULL,
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
    last_indexed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    reg_status char(1) NOT NULL DEFAULT 'A',
    PRIMARY KEY (tenant_id, document_id)
);
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid();
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS document_version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS folder_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS classification_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS classification text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS protocol_number text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS last_indexed_at timestamptz NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';
ALTER TABLE ged.document_search_index ALTER COLUMN search_text SET DEFAULT '';
UPDATE ged.document_search_index SET search_text = '' WHERE search_text IS NULL;

CREATE TABLE IF NOT EXISTS ged.search_query_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NULL,
    query_text text NULL,
    query_hash text NULL,
    interpreted_json jsonb NULL,
    results_count int NOT NULL DEFAULT 0,
    duration_ms int NOT NULL DEFAULT 0,
    clicked_document_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    reg_status char(1) NOT NULL DEFAULT 'A'
);
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS duration_ms int NOT NULL DEFAULT 0;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS clicked_document_id uuid NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.document_access_stat (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NULL,
    document_id uuid NOT NULL,
    source text NOT NULL,
    action text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE ged.document_access_stat ALTER COLUMN source SET DEFAULT 'SMART_SEARCH';
ALTER TABLE ged.document_access_stat ALTER COLUMN action SET DEFAULT 'ACCESS';

CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_document ON ged.document_search_index(tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_folder ON ged.document_search_index(tenant_id, folder_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_year ON ged.document_search_index(tenant_id, extracted_year);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant_age ON ged.document_search_index(tenant_id, extracted_age);
CREATE INDEX IF NOT EXISTS ix_document_search_index_vector ON ged.document_search_index USING GIN(search_vector);
CREATE INDEX IF NOT EXISTS ix_document_search_index_text_trgm ON ged.document_search_index USING GIN(search_text gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_search_query_log_tenant_created ON ged.search_query_log(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_search_query_log_tenant_user ON ged.search_query_log(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS ix_search_query_log_query_hash ON ged.search_query_log(query_hash);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_created ON ged.document_access_stat(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_user ON ged.document_access_stat(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_document ON ged.document_access_stat(tenant_id, document_id);

DO $$
DECLARE seed_tenant uuid;
BEGIN
  FOR seed_tenant IN SELECT DISTINCT tenant_id FROM ged.document WHERE tenant_id IS NOT NULL LOOP
    INSERT INTO ged.search_synonym(tenant_id, term, synonym, category, weight) VALUES
    (seed_tenant,'AVC','acidente vascular cerebral','clinical',1),(seed_tenant,'AVC','derrame','clinical',1),
    (seed_tenant,'diabetes','diabete','clinical',1),(seed_tenant,'diabetes','dm','clinical',1),
    (seed_tenant,'tomografia','tc','exam',1),(seed_tenant,'tomografia','tomografia computadorizada','exam',1),
    (seed_tenant,'raio-x','rx','exam',1),(seed_tenant,'raio-x','radiografia','exam',1),
    (seed_tenant,'câncer','neoplasia','clinical',1),(seed_tenant,'câncer','tumor','clinical',1),
    (seed_tenant,'renal','rim','clinical',1),(seed_tenant,'renal','rins','clinical',1),(seed_tenant,'renal','nefrologia','clinical',1),
    (seed_tenant,'cardíaco','coração','clinical',1),(seed_tenant,'cardíaco','cardiologia','clinical',1),
    (seed_tenant,'ultrassom','ultrassonografia','exam',1),(seed_tenant,'ultrassom','usg','exam',1),
    (seed_tenant,'laboratório','exame laboratorial','exam',1),(seed_tenant,'laboratório','resultado laboratorial','exam',1)
    ON CONFLICT DO NOTHING;
  END LOOP;
END $$;

DO $$
BEGIN
  IF to_regclass('ged.processing_job') IS NOT NULL THEN
    INSERT INTO ged.processing_job(tenant_id, job_type, status, payload, created_at)
    SELECT DISTINCT tenant_id, 'SMART_INDEX', 'PENDING', '{}'::jsonb, now()
    FROM ged.document d
    WHERE d.tenant_id IS NOT NULL
    ON CONFLICT DO NOTHING;
  END IF;
END $$;
