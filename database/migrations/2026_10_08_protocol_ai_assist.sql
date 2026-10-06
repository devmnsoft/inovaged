-- Assistência de IA no Protocolo institucional: revisões, decisões humanas e governança de auditoria.
-- Idempotente, incremental e seguro para replay.

alter type ged.audit_action_enum add value if not exists 'AI_PROTOCOL_SUBJECT_APPLY';
alter type ged.audit_action_enum add value if not exists 'AI_PROTOCOL_DRAFT_APPLY';
alter type ged.audit_action_enum add value if not exists 'AI_PROTOCOL_REVISE';

create table if not exists ged.protocolo_ai_revisao (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocolo_id uuid not null references ged.protocolo(id),
    execution_id uuid not null references ged.ai_execution(id),
    task varchar(64) not null,
    reviewer_id uuid not null,
    decision_type varchar(32) not null,
    decision_fingerprint varchar(64) not null,
    original_suggestion_json jsonb not null default '{}'::jsonb,
    applied_content_json jsonb not null default '{}'::jsonb,
    concurrency_token bigint not null default 0,
    sources_json jsonb not null default '[]'::jsonb,
    coverage_json jsonb not null default '{}'::jsonb,
    notes text null,
    created_at timestamptz not null default now(),
    unique (tenant_id, execution_id, decision_fingerprint)
);

create index if not exists ix_protocolo_ai_revisao_protocolo on ged.protocolo_ai_revisao(tenant_id, protocolo_id, created_at desc);
create index if not exists ix_protocolo_ai_revisao_execution on ged.protocolo_ai_revisao(tenant_id, execution_id);
