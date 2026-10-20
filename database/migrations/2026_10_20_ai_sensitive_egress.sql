-- Existing enabled policies do not implicitly authorize external processing of sensitive content.
ALTER TABLE ged.ai_tenant_policy ADD COLUMN IF NOT EXISTS allow_sensitive_external boolean NOT NULL DEFAULT false;
ALTER TABLE ged.ai_tenant_policy ADD COLUMN IF NOT EXISTS external_authorization_reference text;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='ged.ai_tenant_policy'::regclass AND conname='ck_ai_sensitive_authorization') THEN
        ALTER TABLE ged.ai_tenant_policy ADD CONSTRAINT ck_ai_sensitive_authorization
            CHECK (NOT allow_sensitive_external OR length(btrim(coalesce(external_authorization_reference,''))) BETWEEN 1 AND 500);
    END IF;
END $$;
