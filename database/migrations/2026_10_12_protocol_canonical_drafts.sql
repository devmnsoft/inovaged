create table if not exists ged.protocolo_minuta (
    id uuid primary key,
    tenant_id uuid not null,
    protocolo_id uuid not null,
    setor_id uuid not null,
    titulo varchar(200) not null,
    conteudo text not null,
    status varchar(24) not null default 'RASCUNHO'
        check (status in ('RASCUNHO', 'CONFIRMADA', 'ENCAMINHADA', 'DESCARTADA')),
    versao integer not null default 1 check (versao > 0),
    origem_execucao_id uuid,
    criado_por uuid not null,
    criado_por_nome text not null,
    atualizado_por uuid not null,
    atualizado_por_nome text not null,
    confirmada_por uuid,
    confirmada_em timestamptz,
    encaminhada_por uuid,
    encaminhada_em timestamptz,
    movimento_id uuid,
    descartada_por uuid,
    descartada_em timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    reg_status char(1) not null default 'A',
    constraint ck_protocolo_minuta_titulo check (length(btrim(titulo)) between 1 and 200),
    constraint ck_protocolo_minuta_conteudo check (length(btrim(conteudo)) between 1 and 12000)
);

create index if not exists ix_protocolo_minuta_protocol_status
    on ged.protocolo_minuta (tenant_id, protocolo_id, status, updated_at desc)
    where reg_status = 'A';

create index if not exists ix_protocolo_minuta_sector_status
    on ged.protocolo_minuta (tenant_id, setor_id, status, updated_at desc)
    where reg_status = 'A';

create table if not exists ged.protocolo_minuta_historico (
    id uuid primary key,
    tenant_id uuid not null,
    protocolo_id uuid not null,
    minuta_id uuid not null,
    versao integer not null,
    evento varchar(32) not null,
    status varchar(24) not null,
    titulo varchar(200) not null,
    conteudo text not null,
    usuario_id uuid not null,
    usuario_nome text not null,
    detalhes jsonb not null default '{}'::jsonb
        check (jsonb_typeof(detalhes) = 'object'),
    created_at timestamptz not null default now()
);

create index if not exists ix_protocolo_minuta_history
    on ged.protocolo_minuta_historico (tenant_id, minuta_id, created_at desc);
