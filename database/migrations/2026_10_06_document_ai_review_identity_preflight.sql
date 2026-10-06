-- Run before review_recovery, including upgrades where it is already applied.
-- Published migration bytes and existing identities are deliberately preserved.
alter table ged.ai_suggestion_application add column if not exists operation_key varchar(64);

lock table ged.ai_suggestion_application in share row exclusive mode;
with identities as (
    select id, tenant_id, operation_key, created_at,
           encode(sha256(convert_to(
               replace(tenant_id::text, '-', '') || '|' ||
               replace(execution_id::text, '-', '') || '|' ||
               replace(document_id::text, '-', '') || '|' ||
               replace(version_id::text, '-', '') || '|' ||
               replace(reviewer_id::text, '-', '') || '|' || task, 'UTF8')), 'hex') as canonical_key
    from ged.ai_suggestion_application
), ranked as (
    select *, row_number() over (partition by tenant_id, canonical_key order by created_at, id) as n,
           bool_or(operation_key = canonical_key) over (partition by tenant_id, canonical_key) as has_canonical
    from identities
)
update ged.ai_suggestion_application a
set operation_key = case
    when r.n = 1 and not coalesce(r.has_canonical, false) then r.canonical_key
    else encode(sha256(convert_to(r.canonical_key || '|legacy|' || r.id::text, 'UTF8')), 'hex')
end
from ranked r
where a.id = r.id and a.operation_key is null;

-- No revision, decision, pending item or audit link is deleted or rewritten.
