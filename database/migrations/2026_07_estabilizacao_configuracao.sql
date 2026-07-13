-- Estabilização de configuração, jobs e auditoria administrativa.
create table if not exists ged.worker_execution_state (
  worker_name text not null,
  tenant_id uuid not null,
  enabled boolean not null default true,
  dependency text null,
  last_started_at_utc timestamptz null,
  last_success_at_utc timestamptz null,
  last_error text null,
  duration_ms bigint null,
  processed_count integer null,
  next_run_at_utc timestamptz null,
  status text not null default 'UNKNOWN',
  last_error_correlation_id text null,
  updated_at_utc timestamptz not null default now(),
  primary key (worker_name, tenant_id)
);

create table if not exists ged.configuration_audit_log (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid null,
  configuration_key text not null,
  old_value_masked text null,
  new_value_masked text null,
  user_id uuid null,
  user_name text null,
  ip_address text null,
  correlation_id text null,
  created_at_utc timestamptz not null default now()
);

create index if not exists ix_configuration_audit_log_tenant_created on ged.configuration_audit_log(tenant_id, created_at_utc desc);
create index if not exists ix_worker_execution_state_status on ged.worker_execution_state(status, updated_at_utc desc);
