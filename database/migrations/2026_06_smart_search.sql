-- InovaGED - Busca Inteligente Conversacional (idempotente)
CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

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

ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS term text;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS synonym text;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS category text NULL;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS weight numeric NOT NULL DEFAULT 1;
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE ged.search_synonym ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

CREATE TABLE IF NOT EXISTS ged.document_search_index (
    document_id uuid NOT NULL,
    version_id uuid NULL,
    tenant_id uuid NOT NULL,
    title text NULL,
    file_name text NULL,
    document_type text NULL,
    classification text NULL,
    folder_name text NULL,
    patient_name text NULL,
    medical_record_number text NULL,
    extracted_age int NULL,
    extracted_year int NULL,
    extracted_terms text[] NULL,
    ocr_text text NULL,
    search_text text NULL,
    search_vector tsvector NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, document_id)
);

ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS document_id uuid;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS version_id uuid NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS title text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS file_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS document_type text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS classification text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS folder_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS patient_name text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS medical_record_number text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS extracted_age int NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS extracted_year int NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS extracted_terms text[] NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS ocr_text text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS search_text text NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS search_vector tsvector NULL;
ALTER TABLE ged.document_search_index ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS ged.search_query_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    user_id uuid NULL,
    query_text text NULL,
    query_hash text NULL,
    interpreted_json jsonb NULL,
    results_count int NULL,
    clicked_document_id uuid NULL,
    duration_ms int NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS user_id uuid NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS query_text text NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS query_hash text NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS interpreted_json jsonb NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS results_count int NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS clicked_document_id uuid NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS duration_ms int NULL;
ALTER TABLE ged.search_query_log ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS ged.document_access_stat (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    user_id uuid NULL,
    source text NULL,
    action text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS document_id uuid;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS user_id uuid NULL;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS source text NULL;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS action text NULL;
ALTER TABLE ged.document_access_stat ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

CREATE INDEX IF NOT EXISTS ix_search_synonym_tenant_term ON ged.search_synonym(tenant_id, lower(term));
CREATE INDEX IF NOT EXISTS ix_search_synonym_tenant_synonym ON ged.search_synonym(tenant_id, lower(synonym));
CREATE INDEX IF NOT EXISTS ix_document_search_index_vector ON ged.document_search_index USING GIN(search_vector);
CREATE INDEX IF NOT EXISTS ix_document_search_index_text_trgm ON ged.document_search_index USING GIN(search_text gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_document_search_index_tenant ON ged.document_search_index(tenant_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_document ON ged.document_search_index(document_id);
CREATE INDEX IF NOT EXISTS ix_document_search_index_age ON ged.document_search_index(tenant_id, extracted_age);
CREATE INDEX IF NOT EXISTS ix_document_search_index_year ON ged.document_search_index(tenant_id, extracted_year);
CREATE INDEX IF NOT EXISTS ix_search_query_log_tenant_created ON ged.search_query_log(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_search_query_log_tenant_user ON ged.search_query_log(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_created ON ged.document_access_stat(tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_document ON ged.document_access_stat(tenant_id, document_id);
CREATE INDEX IF NOT EXISTS ix_document_access_stat_tenant_user ON ged.document_access_stat(tenant_id, user_id);

DO $$
DECLARE seed_tenant uuid;
BEGIN
    FOR seed_tenant IN SELECT DISTINCT tenant_id FROM ged.document WHERE tenant_id IS NOT NULL LOOP
        INSERT INTO ged.search_synonym(tenant_id, term, synonym, category, weight)
        VALUES
        (seed_tenant, 'AVC', 'acidente vascular cerebral', 'clinical', 1),
        (seed_tenant, 'diabetes', 'diabete', 'clinical', 1),
        (seed_tenant, 'diabetes', 'DM', 'clinical', 1),
        (seed_tenant, 'tomografia', 'TC', 'exam', 1),
        (seed_tenant, 'tomografia', 'tomografia computadorizada', 'exam', 1),
        (seed_tenant, 'raio x', 'radiografia', 'exam', 1),
        (seed_tenant, 'raio x', 'RX', 'exam', 1),
        (seed_tenant, 'câncer', 'neoplasia', 'clinical', 1),
        (seed_tenant, 'câncer', 'tumor', 'clinical', 1),
        (seed_tenant, 'rim', 'renal', 'clinical', 1),
        (seed_tenant, 'coração', 'cardíaco', 'clinical', 1),
        (seed_tenant, 'coração', 'cardiológico', 'clinical', 1),
        (seed_tenant, 'exame', 'laudo', 'document', 1),
        (seed_tenant, 'exame', 'resultado', 'document', 1)
        ON CONFLICT DO NOTHING;
    END LOOP;
END $$;
