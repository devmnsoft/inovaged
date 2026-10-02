-- Incremental hardening for installations that already applied 2026_10_02.
alter table ged.ai_execution add column if not exists input_fingerprint varchar(64);
alter table ged.ai_execution add column if not exists reservation_period date;
alter table ged.ai_execution add column if not exists sent_at timestamptz;
alter table ged.ai_execution add column if not exists settled_at timestamptz;
alter table ged.ai_execution add column if not exists settled_tokens bigint;
alter table ged.ai_execution add column if not exists usage_estimated boolean not null default false;
alter table ged.ai_execution add column if not exists result_json jsonb;
alter table ged.ai_execution add column if not exists result_expires_at timestamptz;

update ged.ai_execution set input_fingerprint=md5(tenant_id::text||':'||user_id::text||':'||task||':'||idempotency_key)||md5(id::text) where input_fingerprint is null;
update ged.ai_execution set reservation_period=date_trunc('month',created_at)::date where reservation_period is null;
alter table ged.ai_execution alter column input_fingerprint set not null;
alter table ged.ai_execution alter column reservation_period set not null;
alter table ged.ai_execution drop constraint if exists ai_execution_tenant_id_idempotency_key_key;
create unique index if not exists ux_ai_execution_command on ged.ai_execution(tenant_id,user_id,task,idempotency_key);
create index if not exists ix_ai_execution_result_retention on ged.ai_execution(result_expires_at) where result_json is not null;

create or replace function ged.expire_ai_reservations(p_now timestamptz default now()) returns integer language plpgsql as $$
declare affected integer;
begin
  with expired as (
    update ged.ai_execution
       set state=case when sent_at is not null then 'RemoteOutcomeUnknown' else 'Expired' end,
           completed_at=p_now, settled_at=p_now
     where state in ('Reserved','Running') and expires_at<p_now and settled_at is null
     returning tenant_id,reservation_period,reserved_tokens
  ), released as (
    select tenant_id,reservation_period,sum(reserved_tokens) tokens from expired group by tenant_id,reservation_period
  ), applied as (
    update ged.ai_monthly_usage u set reserved_tokens=greatest(0,u.reserved_tokens-r.tokens),updated_at=p_now
      from released r where u.tenant_id=r.tenant_id and u.period_start=r.reservation_period returning 1
  ) select count(*) into affected from expired;
  update ged.ai_execution set result_json=null where result_expires_at<p_now and result_json is not null;
  return affected;
end $$;
