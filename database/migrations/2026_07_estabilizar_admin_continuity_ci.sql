-- InovaGED Evolução 03 - estabilização aditiva de continuidade, jobs e portabilidade.
CREATE SCHEMA IF NOT EXISTS ged;

ALTER TABLE IF EXISTS ged.backup_job ADD COLUMN IF NOT EXISTS finished_at_utc timestamptz NULL;
ALTER TABLE IF EXISTS ged.backup_job ADD COLUMN IF NOT EXISTS started_at_utc timestamptz NULL;
ALTER TABLE IF EXISTS ged.backup_job ADD COLUMN IF NOT EXISTS next_attempt_at_utc timestamptz NULL;
ALTER TABLE IF EXISTS ged.backup_job ADD COLUMN IF NOT EXISTS current_step text NULL;
ALTER TABLE IF EXISTS ged.backup_job ADD COLUMN IF NOT EXISTS progress_percent int NOT NULL DEFAULT 0;
ALTER TABLE IF EXISTS ged.backup_job ADD COLUMN IF NOT EXISTS error_message_masked text NULL;
CREATE INDEX IF NOT EXISTS ix_backup_job_claim_lease
ON ged.backup_job(status, next_attempt_at_utc, locked_until_utc, created_at_utc);

CREATE TABLE IF NOT EXISTS ged.operations_dead_letter (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL,
    job_id uuid NULL,
    job_type text NOT NULL,
    reason text NOT NULL,
    payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    resolved_at_utc timestamptz NULL
);

ALTER TABLE IF EXISTS ged.backup_set ADD COLUMN IF NOT EXISTS location_internal text NULL;
ALTER TABLE IF EXISTS ged.backup_set ADD COLUMN IF NOT EXISTS manifest_json jsonb NULL;
ALTER TABLE IF EXISTS ged.backup_artifact ADD COLUMN IF NOT EXISTS location_internal text NULL;

CREATE TABLE IF NOT EXISTS ged.portability_artifact (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    export_id uuid NOT NULL REFERENCES ged.portability_export(id),
    relative_path text NOT NULL,
    size_bytes bigint NOT NULL,
    sha256 text NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    UNIQUE(export_id, relative_path)
);

DO $$ BEGIN
    IF to_regclass('ged.schema_migration_history') IS NOT NULL THEN
        INSERT INTO ged.schema_migration_history(script_name, notes)
        VALUES ('2026_07_estabilizar_admin_continuity_ci.sql','Estabilização aditiva para claim de jobs, manifestos e artefatos de portabilidade.')
        ON CONFLICT (script_name) DO NOTHING;
    END IF;
END $$;
