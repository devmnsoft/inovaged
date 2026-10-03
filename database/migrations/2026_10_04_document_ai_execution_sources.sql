-- Server-verifiable execution sources and correct expiration/settlement semantics.
-- Additive and safe to re-run. Existing installations keep their checksummed history.
alter table ged.ai_execution add column if not exists source_documents jsonb not null default '[]'::jsonb check (jsonb_typeof(source_documents)='array');
create index if not exists ix_ai_execution_sources on ged.ai_execution using gin(source_documents);

-- Idempotent backfill from the legacy document_refs format "{docId:N}/{verId:N}/extra".
update ged.ai_execution e
   set source_documents=b.docs
  from (
    select c.id,
           jsonb_agg(jsonb_build_object('documentId',split_part(c.ref_text,'/',1),'versionId',split_part(c.ref_text,'/',2)) order by c.ord) as docs
      from (
        select x.id, i.n as ord, (x.document_refs #>> array[((i.n-1))::text]) as ref_text
          from ged.ai_execution x
          cross join lateral generate_series(1,coalesce(jsonb_array_length(x.document_refs),0)) as i(n)
         where x.source_documents='[]'::jsonb
      ) c
     where c.ref_text ~ '^[0-9a-fA-F]{32}/[0-9a-fA-F]{32}'
     group by c.id
  ) b
 where e.id=b.id;

-- Expiration reconciles against the real send stamp: a never-sent request consumed nothing,
-- while a request already sent to the provider settles its reservation as estimated usage.
-- Late responses arriving after settlement are reconciled by the store without consuming twice.
create or replace function ged.expire_ai_reservations(p_now timestamptz default now()) returns integer language plpgsql as $$
declare affected integer;
begin
  with expired as (
    update ged.ai_execution
       set state=case when sent_at is not null then 'RemoteOutcomeUnknown' else 'Expired' end,
           completed_at=p_now, settled_at=p_now,
           settled_tokens=case when sent_at is not null then reserved_tokens else 0 end,
           usage_estimated=case when sent_at is not null then true else usage_estimated end
     where state in ('Reserved','Running') and expires_at<p_now and settled_at is null
     returning tenant_id,reservation_period,reserved_tokens,
               case when sent_at is not null then reserved_tokens else 0 end as consumed_tokens
  ), totals as (
    select tenant_id,reservation_period,
           sum(reserved_tokens) as released_tokens,
           sum(consumed_tokens) as consumed_tokens
      from expired
     group by tenant_id,reservation_period
  ), applied as (
    update ged.ai_monthly_usage u
       set reserved_tokens=greatest(0,u.reserved_tokens-t.released_tokens),
           consumed_tokens=u.consumed_tokens+t.consumed_tokens,
           updated_at=p_now
      from totals t
     where u.tenant_id=t.tenant_id and u.period_start=t.reservation_period
  ) select count(*) into affected from expired;
  update ged.ai_execution set result_json=null where result_expires_at<p_now and result_json is not null;
  return affected;
end $$;
