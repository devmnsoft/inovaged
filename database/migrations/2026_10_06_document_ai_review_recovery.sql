-- Identidade estável da revisão e recuperação durável da temporalidade.
-- Incremental, idempotente e seguro para reexecução. Não altera migrations anteriores.
alter table ged.ai_suggestion_application add column if not exists operation_key varchar(64);

with ranked as (
    select id,
           row_number() over (partition by tenant_id, execution_id, document_id, version_id, reviewer_id, task order by created_at, id) as n,
           encode(sha256(convert_to(
               replace(tenant_id::text, '-', '') || '|' ||
               replace(execution_id::text, '-', '') || '|' ||
               replace(document_id::text, '-', '') || '|' ||
               replace(version_id::text, '-', '') || '|' ||
               replace(reviewer_id::text, '-', '') || '|' ||
               task, 'UTF8')), 'hex') as canonical_key
    from ged.ai_suggestion_application
    where operation_key is null
)
update ged.ai_suggestion_application a
set operation_key = case when ranked.n = 1 then ranked.canonical_key else ranked.canonical_key || '-' || ranked.n::text end
from ranked
where a.id = ranked.id and a.operation_key is null;

create unique index if not exists ux_ai_suggestion_application_operation
    on ged.ai_suggestion_application(tenant_id, operation_key)
    where operation_key is not null;

alter table ged.ai_retention_recalc_pending add column if not exists attempts integer not null default 0;
alter table ged.ai_retention_recalc_pending add column if not exists last_error varchar(200);
alter table ged.ai_retention_recalc_pending add column if not exists claimed_at timestamptz;
alter table ged.ai_retention_recalc_pending add column if not exists claim_token uuid;

create index if not exists ix_ai_retention_recalc_pending_claim
    on ged.ai_retention_recalc_pending(tenant_id, created_at)
    where resolved_at is null;
