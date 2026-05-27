DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='folder_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='created_at') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_doc_tenant_folder_created ON ged.document (tenant_id, folder_id, created_at DESC)';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='pacs' AND table_name='ocr_job' AND column_name='tenant_id')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='pacs' AND table_name='ocr_job' AND column_name='status')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='pacs' AND table_name='ocr_job' AND column_name='requested_at') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ocr_tenant_status_requested ON pacs.ocr_job (tenant_id, status, requested_at DESC)';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='pacs' AND table_name='ocr_job' AND column_name='next_attempt_at') THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ocr_tenant_status_next_attempt ON pacs.ocr_job (tenant_id, status, next_attempt_at)';
    END IF;
END$$;
