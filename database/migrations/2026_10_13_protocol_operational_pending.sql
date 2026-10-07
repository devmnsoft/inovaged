create table if not exists ged.protocolo_pendencia (
    id uuid primary key,
    tenant_id uuid not null,
    protocolo_id uuid not null,
    setor_id uuid not null,
    descricao varchar(500) not null,
    evidencia text,
    fonte_evidencia varchar(500),
    origem varchar(16) not null default 'HUMANA'
        check (origem in ('HUMANA', 'ASSISTENTE_IA')),
    status varchar(16) not null default 'ABERTA'
        check (status in ('ABERTA', 'ATRIBUIDA', 'RESOLVIDA', 'DESCARTADA')),
    execution_id uuid,
    item_index smallint,
    fingerprint char(64),
    confirmada_por uuid not null,
    confirmada_em timestamptz not null default now(),
    atribuida_para uuid,
    atribuida_por uuid,
    atribuida_em timestamptz,
    resolucao text,
    comprovante_documento_id uuid,
    resolvida_por uuid,
    resolvida_em timestamptz,
    motivo_descarte text,
    descartada_por uuid,
    descartada_em timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    reg_status char(1) not null default 'A',
    constraint ck_protocolo_pendencia_descricao check (length(btrim(descricao)) between 1 and 500),
    constraint ck_protocolo_pendencia_evidencia check (evidencia is null or length(evidencia) <= 1000),
    constraint ck_protocolo_pendencia_resolucao check (resolucao is null or length(btrim(resolucao)) <= 2000),
    constraint ck_protocolo_pendencia_descarte check (motivo_descarte is null or length(btrim(motivo_descarte)) <= 1000),
    constraint ck_protocolo_pendencia_source_identity check (
        (execution_id is null and item_index is null and fingerprint is null)
        or (execution_id is not null and item_index is not null and item_index between 0 and 19 and fingerprint is not null)
    )
);

create unique index if not exists ux_protocolo_pendencia_ai_identity
    on ged.protocolo_pendencia (tenant_id, execution_id, item_index)
    where execution_id is not null;

create index if not exists ix_protocolo_pendencia_operational_queue
    on ged.protocolo_pendencia (tenant_id, setor_id, status, updated_at desc)
    where reg_status = 'A';

create index if not exists ix_protocolo_pendencia_protocol_history
    on ged.protocolo_pendencia (tenant_id, protocolo_id, created_at desc)
    where reg_status = 'A';

create table if not exists ged.protocolo_pendencia_historico (
    id uuid primary key,
    tenant_id uuid not null,
    protocolo_id uuid not null,
    pendencia_id uuid not null,
    evento varchar(24) not null,
    status varchar(16) not null,
    descricao varchar(500) not null,
    atribuida_para uuid,
    resolucao text,
    comprovante_documento_id uuid,
    usuario_id uuid not null,
    usuario_nome text not null,
    detalhes jsonb not null default '{}'::jsonb
        check (jsonb_typeof(detalhes) = 'object'),
    created_at timestamptz not null default now()
);

create index if not exists ix_protocolo_pendencia_history
    on ged.protocolo_pendencia_historico (tenant_id, pendencia_id, created_at desc);
