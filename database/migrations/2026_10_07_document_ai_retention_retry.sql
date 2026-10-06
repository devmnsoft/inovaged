alter table ged.ai_retention_recalc_pending
    add column if not exists next_attempt_at timestamptz not null default now();
create index if not exists ix_ai_retention_recalc_pending_due
    on ged.ai_retention_recalc_pending(tenant_id, next_attempt_at, created_at)
    where resolved_at is null and attempts < 10;
