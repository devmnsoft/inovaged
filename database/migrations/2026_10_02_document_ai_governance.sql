-- Persistent, fail-closed tenant AI governance. Additive and safe to re-run.
create table if not exists ged.ai_tenant_policy (
    tenant_id uuid primary key references ged.tenant(id),
    enabled boolean not null default false,
    revision bigint not null default 1,
    allowed_tasks jsonb not null default '[]'::jsonb check (jsonb_typeof(allowed_tasks)='array'),
    allowed_providers jsonb not null default '[]'::jsonb check (jsonb_typeof(allowed_providers)='array'),
    task_models jsonb not null default '{}'::jsonb check (jsonb_typeof(task_models)='object'),
    monthly_token_limit bigint not null default 0 check (monthly_token_limit>=0),
    maximum_input_characters integer not null default 50000 check (maximum_input_characters between 1000 and 500000),
    updated_at timestamptz not null default now(), updated_by uuid null
);

create table if not exists ged.ai_monthly_usage (
    tenant_id uuid not null references ged.tenant(id), period_start date not null,
    consumed_tokens bigint not null default 0 check(consumed_tokens>=0),
    reserved_tokens bigint not null default 0 check(reserved_tokens>=0), updated_at timestamptz not null default now(),
    primary key(tenant_id,period_start)
);

create table if not exists ged.ai_execution (
    id uuid primary key, tenant_id uuid not null references ged.tenant(id), user_id uuid not null,
    task varchar(64) not null, provider varchar(64) not null, model varchar(160) not null,
    idempotency_key varchar(128) not null, policy_revision bigint not null, state varchar(32) not null,
    reserved_tokens bigint not null default 0, reported_input_tokens bigint, reported_output_tokens bigint, reported_total_tokens bigint,
    document_refs jsonb not null default '[]'::jsonb, correlation_id varchar(128), failure_kind varchar(64), limitation varchar(1000),
    created_at timestamptz not null default now(), started_at timestamptz, completed_at timestamptz, expires_at timestamptz not null,
    duration_ms bigint, unique(tenant_id,idempotency_key)
);
create index if not exists ix_ai_execution_tenant_created on ged.ai_execution(tenant_id,created_at desc);
create index if not exists ix_ai_execution_recovery on ged.ai_execution(state,expires_at) where state in ('Reserved','Running');

-- Release abandoned reservations. A remote-running timeout remains uncertain and is never retried automatically.
create or replace function ged.expire_ai_reservations(p_now timestamptz default now()) returns integer language plpgsql as $$
declare affected integer;
begin
  with expired as (
    update ged.ai_execution set state=case when state='Running' then 'RemoteOutcomeUnknown' else 'Expired' end, completed_at=p_now
    where state in ('Reserved','Running') and expires_at<p_now returning tenant_id,reserved_tokens
  ), released as (select tenant_id,sum(reserved_tokens) tokens from expired group by tenant_id)
  update ged.ai_monthly_usage u set reserved_tokens=greatest(0,u.reserved_tokens-r.tokens),updated_at=p_now
  from released r where u.tenant_id=r.tenant_id and u.period_start=date_trunc('month',p_now)::date;
  get diagnostics affected=row_count; return affected;
end $$;
