ALTER TABLE ged.audit_log
ADD COLUMN IF NOT EXISTS event_type text null,
ADD COLUMN IF NOT EXISTS source text null,
ADD COLUMN IF NOT EXISTS details text null,
ADD COLUMN IF NOT EXISTS exception_type text null,
ADD COLUMN IF NOT EXISTS exception_message text null,
ADD COLUMN IF NOT EXISTS stack_trace text null,
ADD COLUMN IF NOT EXISTS path text null,
ADD COLUMN IF NOT EXISTS http_method text null,
ADD COLUMN IF NOT EXISTS http_status integer null,
ADD COLUMN IF NOT EXISTS ip_address text null,
ADD COLUMN IF NOT EXISTS user_agent text null,
ADD COLUMN IF NOT EXISTS elapsed_ms bigint null,
ADD COLUMN IF NOT EXISTS correlation_id text null,
ADD COLUMN IF NOT EXISTS data jsonb null;

CREATE INDEX IF NOT EXISTS ix_audit_log_tenant_event_time
ON ged.audit_log(tenant_id, event_time DESC);
CREATE INDEX IF NOT EXISTS ix_audit_log_event_type ON ged.audit_log(event_type);
CREATE INDEX IF NOT EXISTS ix_audit_log_action ON ged.audit_log(action);
