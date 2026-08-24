CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE IF NOT EXISTS ged.schema_migration_history (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), script_name text NOT NULL, script_path text NULL,
 checksum_sha256 text NULL, applied_at timestamptz NOT NULL DEFAULT now(), applied_by uuid NULL,
 applied_by_name text NULL, source varchar(40) NOT NULL DEFAULT 'MANUAL', success boolean NOT NULL DEFAULT true,
 duration_ms integer NULL, error_message text NULL, notes text NULL, reg_status char(1) NOT NULL DEFAULT 'A');
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS script_path text NULL;
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS checksum_sha256 text NULL;
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS applied_by uuid NULL;
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS applied_by_name text NULL;
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS source varchar(40) NOT NULL DEFAULT 'MANUAL';
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS success boolean NOT NULL DEFAULT true;
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS duration_ms integer NULL;
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS error_message text NULL;
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS notes text NULL;
ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';
CREATE UNIQUE INDEX IF NOT EXISTS ux_schema_migration_history_script_success ON ged.schema_migration_history(script_name) WHERE success = true AND reg_status = 'A';
CREATE INDEX IF NOT EXISTS ix_schema_migration_history_applied_at ON ged.schema_migration_history(applied_at DESC);
