-- Integridade da aplicação assistida e reconciliação de consumo tardio.
-- Incremental, idempotente e seguro para reexecução. Não reescreve migrations anteriores.
alter table ged.ai_execution add column if not exists usage_reconciled_at timestamptz;
alter table ged.ai_execution add column if not exists reconciled_delta bigint;

create table if not exists ged.ai_suggestion_application (
    id uuid primary key,
    tenant_id uuid not null,
    execution_id uuid not null references ged.ai_execution(id),
    document_id uuid not null,
    version_id uuid not null,
    task varchar(64) not null,
    reviewer_id uuid not null,
    decision_fingerprint varchar(64) not null,
    decision_json jsonb not null,
    outcome varchar(32) not null,
    partial boolean not null default false,
    created_at timestamptz not null default now(),
    unique (tenant_id, execution_id, decision_fingerprint)
);
create index if not exists ix_ai_suggestion_application_document on ged.ai_suggestion_application(tenant_id, document_id, created_at desc);

create table if not exists ged.ai_retention_recalc_pending (
    id uuid primary key,
    tenant_id uuid not null,
    document_id uuid not null,
    application_id uuid null,
    reason varchar(500) not null,
    created_at timestamptz not null default now(),
    resolved_at timestamptz null
);
create index if not exists ix_ai_retention_recalc_pending_open on ged.ai_retention_recalc_pending(tenant_id, document_id) where resolved_at is null;
