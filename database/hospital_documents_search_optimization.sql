-- Otimizações para busca hospitalar (PostgreSQL)
DO $$ BEGIN
  CREATE EXTENSION IF NOT EXISTS pg_trgm;
EXCEPTION WHEN insufficient_privilege OR undefined_file THEN
  RAISE NOTICE 'pg_trgm indisponível; índices hospitalares trigram serão ignorados.';
WHEN others THEN RAISE NOTICE 'Não foi possível habilitar pg_trgm: %', SQLERRM;
END $$;

CREATE INDEX IF NOT EXISTS idx_document_search_search_vector_gin
  ON ged.document_search USING gin (search_vector);

CREATE INDEX IF NOT EXISTS idx_document_search_tenant_document_version
  ON ged.document_search (tenant_id, document_id, version_id);

CREATE INDEX IF NOT EXISTS idx_document_tenant_reg_status_status_created
  ON ged.document (tenant_id, reg_status, status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_document_tenant_code
  ON ged.document (tenant_id, code);

CREATE INDEX IF NOT EXISTS idx_document_tenant_title
  ON ged.document (tenant_id, title);

CREATE INDEX IF NOT EXISTS idx_document_version_tenant_document
  ON ged.document_version (tenant_id, document_id);

CREATE INDEX IF NOT EXISTS idx_document_version_tenant_id
  ON ged.document_version (tenant_id, id);

DO $$ DECLARE v_schema text;
BEGIN
  SELECT n.nspname INTO v_schema FROM pg_opclass oc
  JOIN pg_am am ON am.oid=oc.opcmethod JOIN pg_namespace n ON n.oid=oc.opcnamespace
  WHERE am.amname='gin' AND oc.opcname='gin_trgm_ops' LIMIT 1;
  IF v_schema IS NOT NULL THEN
    EXECUTE format('CREATE INDEX IF NOT EXISTS idx_document_title_trgm ON ged.document USING gin (title %I.gin_trgm_ops)', v_schema);
    EXECUTE format('CREATE INDEX IF NOT EXISTS idx_document_code_trgm ON ged.document USING gin (code %I.gin_trgm_ops)', v_schema);
    EXECUTE format('CREATE INDEX IF NOT EXISTS idx_document_search_file_name_trgm ON ged.document_search USING gin (file_name %I.gin_trgm_ops)', v_schema);
  ELSE
    RAISE NOTICE 'gin_trgm_ops indisponível; mantendo FTS/ILIKE.';
  END IF;
END $$;
