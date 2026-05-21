create type if not exists ged.solicitacao_status_enum as enum ('PENDENTE','DEFERIDO','INDEFERIDO','AGUARDAR');
create type if not exists ged.historico_solicitacao_acao_enum as enum ('CRIADO','VISUALIZADO','DEFERIDO','INDEFERIDO','AGUARDAR');

create table if not exists ged.solicitacoes (
    id uuid primary key,
    tenant_id uuid not null,
    usuario_id uuid not null,
    setor_id uuid null,
    arquivo_id uuid null,
    descricao text null,
    status ged.solicitacao_status_enum not null default 'PENDENTE',
    data_solicitacao timestamp not null default now(),
    data_atualizacao timestamp not null default now(),
    admin_id uuid null,
    reg_status char(1) not null default 'A'
);

create index if not exists ix_solicitacoes_tenant_status on ged.solicitacoes(tenant_id, status, data_solicitacao desc);
create index if not exists ix_solicitacoes_usuario on ged.solicitacoes(tenant_id, usuario_id, data_solicitacao desc);
create index if not exists ix_solicitacoes_setor on ged.solicitacoes(tenant_id, setor_id, data_solicitacao desc);

create table if not exists ged.historico_solicitacoes (
    id uuid primary key,
    tenant_id uuid not null,
    solicitacao_id uuid not null references ged.solicitacoes(id),
    usuario_id uuid not null,
    usuario_nome text null,
    acao ged.historico_solicitacao_acao_enum not null,
    comentario text null,
    data timestamp not null default now(),
    reg_status char(1) not null default 'A'
);

create index if not exists ix_historico_solicitacoes_tenant_data on ged.historico_solicitacoes(tenant_id, data desc);
create index if not exists ix_historico_solicitacoes_solicitacao on ged.historico_solicitacoes(solicitacao_id, data desc);
