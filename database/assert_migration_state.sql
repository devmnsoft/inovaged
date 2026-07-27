\set ON_ERROR_STOP on
DO $$
BEGIN
  IF to_regclass('ged.folder') IS NULL OR to_regclass('ged.document') IS NULL OR to_regclass('ged.document_version') IS NULL THEN
    RAISE EXCEPTION 'critical GED tables are missing';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='reg_status') THEN
    RAISE EXCEPTION 'critical ged.document.reg_status column is missing';
  END IF;
  IF to_regclass('ged.ix_document_tenant_reg_status') IS NULL THEN
    RAISE EXCEPTION 'critical document index is missing';
  END IF;
END $$;
