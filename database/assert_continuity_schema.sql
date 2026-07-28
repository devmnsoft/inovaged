\set ON_ERROR_STOP on
DO $$
DECLARE missing text;
BEGIN
  SELECT string_agg(object_name, ', ' ORDER BY object_name) INTO missing
  FROM unnest(ARRAY[
    'backup_policy','backup_job','backup_set','backup_artifact','backup_verification','restore_test',
    'recovery_plan','recovery_plan_version','recovery_test','recovery_objective_measurement',
    'portability_export','portability_export_item','portability_artifact','tenant_offboarding',
    'tenant_offboarding_event','data_retention_hold','operations_worker_heartbeat',
    'operations_dead_letter','operation_job_event']) object_name
  WHERE to_regclass(format('ged.%I', object_name)) IS NULL;
  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'Continuity schema is incomplete. Missing: %', missing USING HINT = E'Execute:\ndotnet run --project InovaGed.Database.Migrator -- apply --verify\n\nOu, usando psql:\npsql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f database/migrations/2026_07_backup_continuity_portability.sql\n\ndatabase/apply_all_required_migrations.sql é um orquestrador exclusivo do cliente psql.';
  END IF;

  SELECT string_agg(column_name, ', ' ORDER BY column_name) INTO missing
  FROM (VALUES ('backup_job','status'),('backup_job','locked_until_utc'),('backup_job','next_attempt_at_utc'),
    ('backup_job','attempts'),('backup_job','max_attempts'),('backup_set','integrity_status'),
    ('backup_set','location_internal'),('backup_set','manifest_path_internal'),('backup_set','checksums_path_internal'),
    ('portability_export','status'),('portability_export','expires_at_utc')) required(table_name,column_name)
  WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema='ged' AND c.table_name=required.table_name AND c.column_name=required.column_name);
  IF missing IS NOT NULL THEN RAISE EXCEPTION 'Continuity schema has missing critical columns: %', missing; END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='ged' AND tablename='backup_job' AND indexdef ILIKE '%status%') THEN
    RAISE EXCEPTION 'Continuity schema has no operational backup_job status index';
  END IF;
END $$;
