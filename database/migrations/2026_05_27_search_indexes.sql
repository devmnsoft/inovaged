DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document' AND column_name='tenant_id') THEN
    EXECUTE 'create index if not exists ix_ged_document_tenant_status_created_at on ged.document(tenant_id, status, created_at desc) where coalesce(reg_status,''A'')=''A''';
    EXECUTE 'create index if not exists ix_ged_document_tenant_folder on ged.document(tenant_id, folder_id) where coalesce(reg_status,''A'')=''A''';
  END IF;
  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='version_id') THEN
    EXECUTE 'create index if not exists ix_ged_document_search_tenant_version on ged.document_search(tenant_id, version_id)';
  END IF;
  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_classification' AND column_name='document_id') THEN
    EXECUTE 'create index if not exists ix_ged_document_classification_tenant_doc on ged.document_classification(tenant_id, document_id) where coalesce(reg_status,''A'')=''A''';
  END IF;
END $$;
