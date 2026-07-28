\set ON_ERROR_STOP on
DO $$
DECLARE missing text;
BEGIN
  SELECT string_agg(required_table, ', ' ORDER BY required_table) INTO missing
  FROM unnest(ARRAY['backup_set','backup_policy','backup_job','backup_verification','recovery_plan','portability_export']) required_table
  WHERE to_regclass(format('ged.%I', required_table)) IS NULL;
  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'Continuity schema is incomplete. Missing: %', missing USING HINT = 'Apply database/migrations/2026_07_backup_continuity_portability.sql';
  END IF;
END $$;
