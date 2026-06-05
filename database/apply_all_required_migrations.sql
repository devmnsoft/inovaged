-- InovaGED pacote mínimo obrigatório para homologação/produção.
-- Execute com psql a partir da raiz do repositório:
--   psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -f database/apply_all_required_migrations.sql

CREATE SCHEMA IF NOT EXISTS ged;

\i database/migrations/2026_06_ged_schema_consolidation.sql
