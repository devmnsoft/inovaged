-- InovaGED pacote mínimo obrigatório para homologação/produção.
-- Execute com psql a partir da raiz do repositório:
--   psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -f database/apply_all_required_migrations.sql
-- Script master idempotente: não remove tabelas, não executa DROP e só delega para migrations seguras.

-- Histórico de migrations / schema base
CREATE SCHEMA IF NOT EXISTS ged;

-- GED, Auditoria, Upload batch, Upload chunked, Documento parcial, OCR e Índices
\i database/migrations/2026_06_ged_schema_consolidation.sql
