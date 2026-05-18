-- Otimizações para busca hospitalar (PostgreSQL)
CREATE EXTENSION IF NOT EXISTS pg_trgm;

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

CREATE INDEX IF NOT EXISTS idx_document_title_trgm
  ON ged.document USING gin (title gin_trgm_ops);

CREATE INDEX IF NOT EXISTS idx_document_code_trgm
  ON ged.document USING gin (code gin_trgm_ops);

CREATE INDEX IF NOT EXISTS idx_document_search_file_name_trgm
  ON ged.document_search USING gin (file_name gin_trgm_ops);
