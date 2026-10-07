using System.Security.Claims;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Identity;
using InovaGed.Application.Protocolo;
using InovaGed.Application.Security;
using InovaGed.Infrastructure.ArtificialIntelligence;
using InovaGed.Infrastructure.Audit;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Ged.Loans;
using InovaGed.Infrastructure.Protocolo;
using InovaGed.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace InovaGed.Application.Tests;

[Collection("Document AI PostgreSQL")]
public sealed class ProtocolAiAssistPostgresTests : IAsyncLifetime
{
    private NpgsqlConnection? _admin;

    public async Task InitializeAsync()
    {
        System.Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Homologation");
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        if (PgGate.UnavailableReason is not null) return;
        _admin = new NpgsqlConnection(PgGate.Dsn());
        await _admin.OpenAsync();

        const string schema = """
create schema if not exists ged;
create extension if not exists pgcrypto;
create table if not exists ged.tenant(id uuid primary key, name text);
create table if not exists ged.app_user(id uuid primary key, tenant_id uuid, name text, email text, reg_status text default 'A', is_active boolean default true, deleted_at_utc timestamptz);
create table if not exists ged.app_role(id uuid, tenant_id uuid);
create table if not exists ged.role(id uuid, tenant_id uuid);
create table if not exists ged.user_role(user_id uuid, role_id uuid);
create table if not exists ged.permission(code text, reg_status text default 'A');
create table if not exists ged.role_permission(role_id uuid, tenant_id uuid, permission_code text, reg_status text default 'A');
create table if not exists ged.document_acl(document_id uuid, user_id uuid, role_id uuid, can_read boolean, can_write boolean);

create table if not exists ged.protocolo_setor(id uuid primary key, tenant_id uuid not null, nome text not null, sigla text, ativo boolean not null default true, reg_status char(1) not null default 'A');
create table if not exists ged.protocolo_usuario_setor(id uuid primary key, tenant_id uuid not null, usuario_id uuid not null, setor_id uuid not null, ativo boolean not null default true, pode_receber boolean not null default true, pode_tramitar boolean not null default true, pode_decidir boolean not null default true, reg_status char(1) not null default 'A');
create table if not exists ged.protocolo_setor_participante(tenant_id uuid not null, protocolo_id uuid not null, setor_id uuid not null, pode_visualizar boolean not null default true, pode_editar boolean not null default false, participou_em timestamptz not null default now(), primary key (tenant_id, protocolo_id, setor_id));

create table if not exists ged.protocolo(
    id uuid primary key, tenant_id uuid not null, numero text not null, assunto text not null, descricao text,
    status text not null default 'CRIADO', prioridade text default 'NORMAL', setor_atual_id uuid, setor_origem_id uuid,
    situacao_custodia text,
    created_at timestamptz not null default now(), updated_at timestamptz, updated_by uuid, reg_status char(1) not null default 'A'
);

create table if not exists ged.protocolo_tramitacao(
    id uuid default gen_random_uuid() primary key, tenant_id uuid not null, protocolo_id uuid not null,
    setor_origem_id uuid, setor_origem_nome text, setor_destino_id uuid, setor_destino_nome text,
    usuario_id uuid, usuario_nome text, acao text not null, status_anterior text, status_novo text,
    despacho text, observacao text, justificativa text, data_tramitacao timestamptz not null default now(),
    ip text, user_agent text, situacao_movimentacao text, protocolo_documento_id uuid,
    idempotency_key text, correlation_id text, ativa boolean not null default true,
    prazo_em timestamptz, entregue_a text, responsavel_destino_id uuid,
    recebida_por uuid, reg_status char(1) not null default 'A'
);

create table if not exists ged.protocolo_documento_ged(
    id uuid primary key, tenant_id uuid not null, protocolo_id uuid not null, ged_document_id uuid not null,
    vinculado_por uuid, vinculado_em timestamptz not null default now(), reg_status char(1) not null default 'A'
);

create table if not exists ged.protocolo_observacao(
    id uuid default gen_random_uuid() primary key, tenant_id uuid not null, protocolo_id uuid not null,
    setor_id uuid, setor_nome text, usuario_id uuid, usuario_nome text, tipo text not null default 'PUBLICA',
    observacao text not null, created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);

create table if not exists ged.protocolo_minuta(
    id uuid primary key, tenant_id uuid not null, protocolo_id uuid not null, setor_id uuid not null,
    titulo text not null, conteudo text not null, status text not null, versao integer not null,
    origem_execucao_id uuid, criado_por uuid not null, criado_por_nome text not null,
    atualizado_por uuid not null, atualizado_por_nome text not null, confirmada_por uuid,
    confirmada_em timestamptz, encaminhada_por uuid, encaminhada_em timestamptz,
    movimento_id uuid, descartada_por uuid, descartada_em timestamptz,
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

create table if not exists ged.protocolo_minuta_historico(
    id uuid primary key, tenant_id uuid not null, protocolo_id uuid not null, minuta_id uuid not null,
    versao integer not null, evento text not null, status text not null, titulo text not null,
    conteudo text not null, usuario_id uuid not null, usuario_nome text not null,
    detalhes jsonb not null default '{}'::jsonb, created_at timestamptz not null default now()
);

create table if not exists ged.protocolo_pendencia(
    id uuid primary key, tenant_id uuid not null, protocolo_id uuid not null, setor_id uuid not null,
    descricao varchar(500) not null, evidencia text, fonte_evidencia varchar(500),
    origem varchar(16) not null default 'HUMANA', status varchar(16) not null default 'ABERTA',
    execution_id uuid, item_index smallint, fingerprint char(64), confirmada_por uuid not null,
    confirmada_em timestamptz not null default now(), atribuida_para uuid, atribuida_por uuid,
    atribuida_em timestamptz, resolucao text, comprovante_documento_id uuid,
    resolvida_por uuid, resolvida_em timestamptz, motivo_descarte text, descartada_por uuid,
    descartada_em timestamptz, created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);

create unique index if not exists ux_test_protocolo_pendencia_ai_identity
    on ged.protocolo_pendencia(tenant_id, execution_id, item_index) where execution_id is not null;

create table if not exists ged.protocolo_pendencia_historico(
    id uuid primary key, tenant_id uuid not null, protocolo_id uuid not null, pendencia_id uuid not null,
    evento text not null, status text not null, descricao varchar(500) not null, atribuida_para uuid,
    resolucao text, comprovante_documento_id uuid, usuario_id uuid not null, usuario_nome text not null,
    detalhes jsonb not null default '{}'::jsonb, created_at timestamptz not null default now()
);

create table if not exists ged.protocolo_ai_revisao (
    id uuid primary key,
    tenant_id uuid not null,
    protocolo_id uuid not null,
    execution_id uuid not null,
    task text not null,
    reviewer_id uuid not null,
    decision_type text not null,
    decision_fingerprint text not null,
    original_suggestion_json jsonb,
    applied_content_json jsonb,
    concurrency_token bigint,
    notes text,
    created_at timestamptz not null default now()
);

create table if not exists ged.document(
    id uuid primary key, tenant_id uuid not null, title text, description text,
    is_confidential boolean not null default false, current_version_id uuid,
    reg_status char(1) not null default 'A', created_at timestamptz not null default now()
);

create table if not exists ged.document_version(
    id uuid primary key, tenant_id uuid not null, document_id uuid not null,
    version_number int not null default 1, file_name text, size_bytes bigint default 0,
    storage_path text, created_at timestamptz not null default now()
);

create table if not exists ged.document_search(
    document_id uuid primary key, tenant_id uuid not null, ocr_text text, indexed_at timestamptz not null default now()
);

create table if not exists ged.app_audit_log(
    id uuid default gen_random_uuid() primary key, tenant_id uuid, user_id uuid, user_name text,
    action text not null, event_type text not null default 'INFO', source text, entity_name text,
    entity_id text, method text, path text, status_code integer, message text, details jsonb, correlation_id text, ip_address text, user_agent text,
    created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);

create table if not exists ged.ai_execution (
    id uuid primary key, tenant_id uuid not null, user_id uuid not null, task text not null,
    provider text not null, model text not null, idempotency_key text, input_fingerprint text,
    policy_revision int default 1, state text not null default 'Completed', reserved_tokens bigint default 0,
    reservation_period date default current_date, expires_at timestamptz default now() + interval '1 day',
    source_documents jsonb not null default '[]'::jsonb, document_refs jsonb not null default '[]'::jsonb,
    context_metadata jsonb,
    started_at timestamptz, sent_at timestamptz, completed_at timestamptz, settled_at timestamptz,
    duration_ms bigint, reported_input_tokens bigint, reported_output_tokens bigint, reported_total_tokens bigint,
    settled_tokens bigint, usage_estimated boolean, result_json jsonb, result_expires_at timestamptz,
    failure_kind text, limitation text, correlation_id text,
    usage_reconciled_at timestamptz, reconciled_delta bigint,
    unique(tenant_id,user_id,task,idempotency_key)
);

create table if not exists ged.ai_tenant_policy (
    tenant_id uuid primary key, revision bigint not null default 1, enabled boolean not null default true,
    allowed_tasks jsonb not null default '[]'::jsonb, allowed_providers jsonb not null default '[]'::jsonb,
    task_models jsonb not null default '{}'::jsonb, monthly_token_limit bigint not null default 1000000,
    maximum_input_characters int not null default 50000
);

create table if not exists ged.ai_monthly_usage (
    tenant_id uuid not null, period_start date not null, consumed_tokens bigint not null default 0,
    reserved_tokens bigint not null default 0, updated_at timestamptz not null default now(),
    primary key(tenant_id, period_start)
);

alter table ged.tenant add column if not exists code text;
alter table ged.app_user add column if not exists password_hash text;
alter table ged.app_role add column if not exists name text;
alter table ged.app_role add column if not exists normalized_name text;
alter table ged.role add column if not exists code text;
alter table ged.role add column if not exists name text;
alter table ged.role add column if not exists reg_status text default 'A';
alter table ged.permission add column if not exists name text;
alter table ged.protocolo_documento_ged add column if not exists criado_por uuid;
alter table ged.protocolo_documento_ged add column if not exists created_at timestamptz not null default now();
alter table ged.document add column if not exists code text;
alter table ged.document_version add column if not exists file_extension text;
alter table ged.document_version add column if not exists file_size_bytes bigint;
alter table ged.document_search add column if not exists version_id uuid;
alter table ged.document_search add column if not exists search_vector tsvector;
""";
        await _admin.ExecuteAsync(schema);
    }

    public async Task DisposeAsync()
    {
        if (_admin is not null) await _admin.DisposeAsync();
    }

    private IDbConnectionFactory Factory() => new NpgsqlConnectionFactory(PgGate.Dsn());

    private sealed class TestCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string Email { get; set; } = "revisor@inovaged.local";
        public IReadOnlyList<string> Roles { get; set; } = ["User"];
    }

    private sealed class TestAbacAuthorizationService : IAbacAuthorizationService
    {
        public Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class ProtocolFixture
    {
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public Guid SetorId { get; set; }
        public Guid ProtocoloId { get; set; }
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string ProtocolNumber { get; set; } = string.Empty;
    }

    private async Task<ProtocolFixture> SeedProtocolAsync()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var setor = Guid.NewGuid();
        var proto = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var version = Guid.NewGuid();
        var num = "PROT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        // 1. Tenant, User, Setor e Vínculo
        await _admin!.ExecuteAsync("insert into ged.tenant(id,name,code) values(@tenant,'Tenant AI',@tenant::text) on conflict do nothing", new { tenant });
        await _admin!.ExecuteAsync("""
insert into ged.ai_tenant_policy(tenant_id,revision,enabled,allowed_tasks,allowed_providers,task_models,monthly_token_limit,maximum_input_characters)
values(@tenant,1,true,'["SupportProtocol"]'::jsonb,'["Deterministic"]'::jsonb,'{"SupportProtocol":"deterministic-v1"}'::jsonb,1000000,50000)
on conflict (tenant_id) do update set enabled=true, allowed_tasks=excluded.allowed_tasks, allowed_providers=excluded.allowed_providers, task_models=excluded.task_models
""", new { tenant });
        await _admin!.ExecuteAsync("insert into ged.app_user(id,tenant_id,name,email,password_hash,is_active) values(@user,@tenant,'Revisor AI','revisor@inovaged.local','hash',true) on conflict do nothing", new { user, tenant });
        await _admin!.ExecuteAsync("insert into ged.protocolo_setor(id,tenant_id,nome,sigla,ativo,reg_status) values(@setor,@tenant,'Gabinete','GAB',true,'A')", new { setor, tenant });
        await _admin!.ExecuteAsync("insert into ged.protocolo_usuario_setor(id,tenant_id,usuario_id,setor_id,ativo,reg_status) values(@id,@tenant,@user,@setor,true,'A')", new { id = Guid.NewGuid(), tenant, user, setor });

        var role = Guid.NewGuid();
        await _admin!.ExecuteAsync("insert into ged.app_role(id,tenant_id,name,normalized_name) values(@role,@tenant,'Role AI','ROLE_AI')", new { role, tenant });
        await _admin!.ExecuteAsync("insert into ged.role(id,tenant_id,code,name,reg_status) values(@role,@tenant,'ROLE_AI','Role AI','A')", new { role, tenant });
        await _admin!.ExecuteAsync("insert into ged.user_role(user_id,role_id) values(@user,@role)", new { user, role });
        await _admin!.ExecuteAsync("insert into ged.permission(code,name,reg_status) values('Documents.View','View Docs','A'),('GED.DOCUMENTS','GED Docs','A') on conflict do nothing");
        await _admin!.ExecuteAsync("insert into ged.role_permission(role_id,tenant_id,permission_code,reg_status) values(@role,@tenant,'Documents.View','A'),(@role,@tenant,'GED.DOCUMENTS','A')", new { role, tenant });

        // 2. Protocolo institucional
        await _admin!.ExecuteAsync("""
insert into ged.protocolo(id,tenant_id,numero,assunto,descricao,status,prioridade,setor_atual_id,setor_origem_id,created_at,updated_at,reg_status)
values(@proto,@tenant,@num,'Assunto Original de Teste','Descrição detalhada do processo institucional','TRAMITANDO','NORMAL',@setor,@setor,now(),now(),'A')
""", new { proto, tenant, num, setor });

        // 3. Documento GED com Versão e OCR
        await _admin!.ExecuteAsync("""
insert into ged.document(id,tenant_id,code,title,is_confidential,current_version_id,reg_status,created_at)
values(@doc,@tenant,'DOC-TEST','Ofício 123',false,@version,'A',now())
""", new { doc, tenant, version });
        await _admin!.ExecuteAsync("""
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path)
values(@version,@tenant,@doc,1,'doc.pdf','.pdf',1024,'docs/doc.pdf')
""", new { version, tenant, doc });
        await _admin!.ExecuteAsync("""
insert into ged.document_search(document_id,tenant_id,version_id,ocr_text,search_vector)
values(@doc,@tenant,@version,'Texto integral do ofício requisitando parecer técnico institucional.',to_tsvector('portuguese','Texto integral do ofício requisitando parecer técnico institucional.'))
""", new { doc, tenant, version });

        // 4. Vínculo do Documento GED ao Protocolo
        await _admin!.ExecuteAsync("""
insert into ged.protocolo_documento_ged(id,tenant_id,protocolo_id,ged_document_id,criado_por,created_at,reg_status)
values(@id,@tenant,@proto,@doc,@user,now(),'A')
""", new { id = Guid.NewGuid(), tenant, proto, doc, user });

        return new ProtocolFixture
        {
            TenantId = tenant,
            UserId = user,
            SetorId = setor,
            ProtocoloId = proto,
            DocumentId = doc,
            VersionId = version,
            ProtocolNumber = num
        };
    }

    private ProtocolAiAssistService CreateService(TestCurrentUser current)
    {
        var factory = Factory();
        var options = Options.Create(new DocumentAiOptions
        {
            Enabled = true,
            Provider = "Deterministic",
            TaskModels = { ["SupportProtocol"] = "deterministic-v1" }
        });
        var rawGateway = new DocumentAiGateway(new System.Net.Http.HttpClient(), options, NullLogger<DocumentAiGateway>.Instance);
        var gateway = new GovernedDocumentAiGateway(rawGateway, new PostgresAiGovernanceStore(factory));
        var protocolAccess = new ProtocolAccessService(factory);
        var auth = new AbacAuthorizationService(factory);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var audit = new AuditWriter(factory, NullLogger<AuditWriter>.Instance, config);
        var httpContextAccessor = new HttpContextAccessor();

        return new ProtocolAiAssistService(factory, current, gateway, protocolAccess, auth, audit, httpContextAccessor, NullLogger<ProtocolAiAssistService>.Instance);
    }

    [PgGatedFact]
    public async Task Assist_generates_only_the_requested_modality_with_sources_and_concurrency_token()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);

        var result = await service.AssistAsync(new ProtocolAiAssistRequest
        {
            ProtocoloId = fx.ProtocoloId,
            TaskKind = "SUMMARY"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.ExecutionId);
        Assert.True(result.ReviewRequired);
        Assert.True(result.ConcurrencyToken > 0);
        Assert.Equal(fx.ProtocolNumber, result.ProtocolNumber);
        Assert.Equal("SummarizeProcess", result.RequestedTaskKind);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.Null(result.SuggestedSubject);
        Assert.Null(result.DispatchDraft);
        Assert.Empty(result.PendingItems);
        Assert.True(result.Sources.Count >= 2); // Processo + Documento GED
        Assert.Contains(result.Sources, s => s.SourceType == "PROTOCOLO");
        Assert.Contains(result.Sources, s => s.SourceType == "GED_DOCUMENT" && s.HasOcr);
    }

    [PgGatedFact]
    public async Task Assist_blocks_user_without_sector_access_or_wrong_tenant()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();

        // Usuário de outro tenant
        var otherTenantUser = new TestCurrentUser { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var serviceOtherTenant = CreateService(otherTenantUser);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            serviceOtherTenant.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId }, CancellationToken.None));

        // Usuário do mesmo tenant mas sem vínculo ao setor e sem papel admin
        var unauthorizedUser = Guid.NewGuid();
        await _admin!.ExecuteAsync("insert into ged.app_user(id,tenant_id,name,email,password_hash,is_active) values(@user,@tenant,'Sem Acesso','noaccess@inovaged.local','hash',true)", new { user = unauthorizedUser, tenant = fx.TenantId });
        var currentNoAccess = new TestCurrentUser { TenantId = fx.TenantId, UserId = unauthorizedUser, Roles = ["User"] };
        var serviceNoAccess = CreateService(currentNoAccess);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            serviceNoAccess.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId }, CancellationToken.None));
    }

    [PgGatedFact]
    public async Task Apply_rejects_execution_from_another_protocol_with_the_same_document()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);
        var assist = await service.AssistAsync(new ProtocolAiAssistRequest
        {
            ProtocoloId = fx.ProtocoloId,
            TaskKind = "SUBJECT"
        }, CancellationToken.None);

        var otherProtocolId = Guid.NewGuid();
        await _admin!.ExecuteAsync("""
insert into ged.protocolo(id,tenant_id,numero,assunto,descricao,status,prioridade,setor_atual_id,setor_origem_id,created_at,updated_at,reg_status)
values(@id,@tenant,'PROT-OUTRO','Outro assunto','Outro processo','TRAMITANDO','NORMAL',@sector,@sector,now(),now(),'A')
""", new { id = otherProtocolId, tenant = fx.TenantId, sector = fx.SetorId });
        await _admin.ExecuteAsync("""
insert into ged.protocolo_setor_participante(tenant_id,protocolo_id,setor_id,pode_visualizar,pode_editar)
values(@tenant,@id,@sector,true,true)
""", new { id = otherProtocolId, tenant = fx.TenantId, sector = fx.SetorId });
        await _admin.ExecuteAsync("""
insert into ged.protocolo_documento_ged(id,tenant_id,protocolo_id,ged_document_id,vinculado_por,reg_status)
values(@link,@tenant,@id,@document,@user,'A')
""", new { id = otherProtocolId, tenant = fx.TenantId, link = Guid.NewGuid(), document = fx.DocumentId, user = fx.UserId });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplySubjectAsync(new ProtocolAiApplySubjectRequest
        {
            ProtocoloId = otherProtocolId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            Subject = "Assunto de outro protocolo",
            Accepted = true
        }, CancellationToken.None));
    }

    [PgGatedFact]
    public async Task Apply_rejects_a_modality_not_requested_for_the_execution()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);
        var assist = await service.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId, TaskKind = "SUMMARY" }, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplySubjectAsync(new ProtocolAiApplySubjectRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            Subject = "Não autorizado",
            Accepted = true
        }, CancellationToken.None));
    }

    [PgGatedFact]
    public async Task Apply_rejects_a_source_after_its_current_version_changes()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);
        var assist = await service.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId, TaskKind = "SUBJECT" }, CancellationToken.None);
        var newVersion = Guid.NewGuid();
        await _admin!.ExecuteAsync("""
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path)
values(@version,@tenant,@document,2,'doc-v2.pdf','.pdf',1024,'docs/doc-v2.pdf');
update ged.document set current_version_id=@version where tenant_id=@tenant and id=@document;
update ged.document_search set version_id=@version, ocr_text='Texto novo da versão posterior.'
where tenant_id=@tenant and document_id=@document;
""", new { version = newVersion, tenant = fx.TenantId, document = fx.DocumentId });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplySubjectAsync(new ProtocolAiApplySubjectRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            Subject = "Assunto obsoleto",
            Accepted = true
        }, CancellationToken.None));
    }

    [PgGatedFact]
    public async Task Apply_fails_closed_when_persisted_source_json_is_malformed()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);
        var assist = await service.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId, TaskKind = "SUBJECT" }, CancellationToken.None);
        await _admin!.ExecuteAsync("update ged.ai_execution set source_documents='{\"documentId\":\"not-an-array\"}'::jsonb where tenant_id=@tenant and id=@execution", new { tenant = fx.TenantId, execution = assist.ExecutionId });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplySubjectAsync(new ProtocolAiApplySubjectRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            Subject = "Assunto com fonte corrompida",
            Accepted = true
        }, CancellationToken.None));
    }

    [PgGatedFact]
    public async Task Apply_subject_persists_revision_audits_and_updates_protocol()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);

        var assist = await service.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId }, CancellationToken.None);
        var novoAssunto = "Assunto Revisado por Humano";

        var applied = await service.ApplySubjectAsync(new ProtocolAiApplySubjectRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            Subject = novoAssunto,
            Accepted = true
        }, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.False(applied.AlreadyApplied);
        Assert.Equal(novoAssunto, applied.AppliedContent);

        // Conferir no banco de dados
        var subjectDb = await _admin!.ExecuteScalarAsync<string>("select assunto from ged.protocolo where id=@id", new { id = fx.ProtocoloId });
        Assert.Equal(novoAssunto, subjectDb);

        var revCount = await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.protocolo_ai_revisao where protocolo_id=@id and task='SUGGEST_SUBJECT'", new { id = fx.ProtocoloId });
        Assert.Equal(1, revCount);

        var auditCount = await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.app_audit_log where action='AI_PROTOCOL_SUBJECT_APPLY' and entity_id=@id", new { id = fx.ProtocoloId.ToString() });
        Assert.Equal(1, auditCount);

        // Replay: mesma decisão não duplica efeitos
        var replay = await service.ApplySubjectAsync(new ProtocolAiApplySubjectRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            Subject = novoAssunto,
            Accepted = true
        }, CancellationToken.None);

        Assert.True(replay.Success);
        Assert.True(replay.AlreadyApplied);
        Assert.Equal(1, await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.protocolo_ai_revisao where protocolo_id=@id and task='SUGGEST_SUBJECT'", new { id = fx.ProtocoloId }));
        Assert.Equal(1, await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.app_audit_log where action='AI_PROTOCOL_SUBJECT_APPLY' and entity_id=@id", new { id = fx.ProtocoloId.ToString() }));

        // Conflito de decisão diferente na mesma execução
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplySubjectAsync(new ProtocolAiApplySubjectRequest
            {
                ProtocoloId = fx.ProtocoloId,
                ExecutionId = assist.ExecutionId,
                ConcurrencyToken = assist.ConcurrencyToken,
                Subject = "Outro Assunto Totalmente Diferente",
                Accepted = false
            }, CancellationToken.None));
    }

    [PgGatedFact]
    public async Task Concurrent_replay_of_the_same_subject_decision_is_read_only()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);
        var assist = await service.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId }, CancellationToken.None);
        var request = new ProtocolAiApplySubjectRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            Subject = "Assunto concorrente",
            Accepted = true
        };

        var results = await Task.WhenAll(
            service.ApplySubjectAsync(request, CancellationToken.None),
            service.ApplySubjectAsync(request, CancellationToken.None));

        Assert.Single(results.Where(x => !x.AlreadyApplied));
        Assert.Single(results.Where(x => x.AlreadyApplied));
        Assert.Equal(1, await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.protocolo_ai_revisao where protocolo_id=@id and task='SUGGEST_SUBJECT'", new { id = fx.ProtocoloId }));
        Assert.Equal(1, await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.app_audit_log where action='AI_PROTOCOL_SUBJECT_APPLY' and entity_id=@id", new { id = fx.ProtocoloId.ToString() }));
    }

    [PgGatedFact]
    public async Task Apply_draft_saves_canonical_draft_without_auto_dispatch_or_signing()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);

        var assist = await service.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId }, CancellationToken.None);
        var minutaTexto = "Minuta de Despacho Requisitando Informações Complementares.";

        var applied = await service.ApplyDispatchDraftAsync(new ProtocolAiApplyDraftRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            DraftText = minutaTexto,
            Accepted = true
        }, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.False(applied.AlreadyApplied);
        Assert.NotNull(applied.DraftId);
        Assert.Equal(minutaTexto, applied.AppliedContent);

        // Status do protocolo DEVE permanecer TRAMITANDO (não encerra, não defere silenciosamente)
        var statusDb = await _admin!.ExecuteScalarAsync<string>("select status from ged.protocolo where id=@id", new { id = fx.ProtocoloId });
        Assert.Equal("TRAMITANDO", statusDb);

        var draft = await _admin!.QuerySingleAsync<(string Status, string Conteudo, int Versao)>(
            "select status as \"Status\", conteudo as \"Conteudo\", versao as \"Versao\" from ged.protocolo_minuta where id=@id",
            new { id = applied.DraftId });
        Assert.Equal("RASCUNHO", draft.Status);
        Assert.Equal(minutaTexto, draft.Conteudo);
        Assert.Equal(1, draft.Versao);
        Assert.Equal(1, await _admin.ExecuteScalarAsync<int>("select count(*) from ged.protocolo_minuta_historico where minuta_id=@id and evento='CRIADA_IA'", new { id = applied.DraftId }));
        Assert.Equal(0, await _admin.ExecuteScalarAsync<int>("select count(*) from ged.protocolo_observacao where protocolo_id=@id and tipo='DESPACHO'", new { id = fx.ProtocoloId }));

        var revCount = await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.protocolo_ai_revisao where protocolo_id=@id and task='PREPARE_DISPATCH_DRAFT'", new { id = fx.ProtocoloId });
        Assert.Equal(1, revCount);

        var auditCount = await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.app_audit_log where action='AI_PROTOCOL_DRAFT_APPLY' and entity_id=@id", new { id = fx.ProtocoloId.ToString() });
        Assert.Equal(1, auditCount);

        // Replay idempotente
        var replay = await service.ApplyDispatchDraftAsync(new ProtocolAiApplyDraftRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            DraftText = minutaTexto,
            Accepted = true
        }, CancellationToken.None);

        Assert.True(replay.Success);
        Assert.True(replay.AlreadyApplied);
        Assert.Equal(applied.DraftId, replay.DraftId);
        Assert.Equal(1, await _admin!.ExecuteScalarAsync<int>("select count(*) from ged.protocolo_minuta where protocolo_id=@id", new { id = fx.ProtocoloId }));

        // Conflito de concorrência com token defasado
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyDispatchDraftAsync(new ProtocolAiApplyDraftRequest
            {
                ProtocoloId = fx.ProtocoloId,
                ExecutionId = assist.ExecutionId,
                ConcurrencyToken = assist.ConcurrencyToken - 5000,
                DraftText = "Tentativa de minuta em estado defasado",
                Accepted = true
            }, CancellationToken.None));
    }

    [PgGatedFact]
    public async Task Forward_confirmed_draft_creates_official_movement_atomically_and_is_idempotent()
    {
        var fx = await SeedProtocolAsync();
        var destination = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        const string draftText = "Despacho revisado e confirmado pelo setor.";
        await _admin!.ExecuteAsync(
            "insert into ged.protocolo_setor(id,tenant_id,nome,sigla,ativo,reg_status) values(@destination,@tenant,'Destino','DST',true,'A')",
            new { destination, tenant = fx.TenantId });
        await _admin.ExecuteAsync("""
insert into ged.protocolo_minuta (
    id, tenant_id, protocolo_id, setor_id, titulo, conteudo, status, versao,
    criado_por, criado_por_nome, atualizado_por, atualizado_por_nome,
    confirmada_por, confirmada_em, created_at, updated_at, reg_status
) values (
    @draftId, @tenantId, @protocolId, @sectorId, 'Minuta confirmada', @content, 'CONFIRMADA', 2,
    @userId, 'Revisor AI', @userId, 'Revisor AI', @userId, now(), now(), now(), 'A'
);
""", new
        {
            draftId,
            tenantId = fx.TenantId,
            protocolId = fx.ProtocoloId,
            sectorId = fx.SetorId,
            content = draftText,
            userId = fx.UserId
        });

        var service = new InovaGed.Infrastructure.Ged.Protocols.ProtocoloCentralService(
            Factory(),
            NullLogger<InovaGed.Infrastructure.Ged.Protocols.ProtocoloCentralService>.Instance,
            new TestAbacAuthorizationService());
        var actor = new ProtocoloActor
        {
            TenantId = fx.TenantId,
            UserId = fx.UserId,
            UserName = "Revisor AI"
        };
        var idempotencyKey = $"protocolo-minuta:{draftId:N}:forward";
        var result = await service.ForwardAsync(
            actor, fx.ProtocoloId, null, destination, null, null, idempotencyKey,
            null, null, null, CancellationToken.None, draftId);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.MovimentoId);
        var movement = await _admin.QuerySingleAsync<(string Despacho, Guid? DestinoId)>(
            "select despacho as \"Despacho\", setor_destino_id as \"DestinoId\" from ged.protocolo_tramitacao where id=@id",
            new { id = result.MovimentoId });
        Assert.Equal(draftText, movement.Despacho);
        Assert.Equal(destination, movement.DestinoId);
        Assert.Equal("ENCAMINHADA", await _admin.ExecuteScalarAsync<string>(
            "select status from ged.protocolo_minuta where id=@id", new { id = draftId }));
        Assert.Equal(result.MovimentoId, await _admin.ExecuteScalarAsync<Guid?>(
            "select movimento_id from ged.protocolo_minuta where id=@id", new { id = draftId }));
        Assert.Equal(1, await _admin.ExecuteScalarAsync<int>(
            "select count(*) from ged.protocolo_minuta_historico where minuta_id=@id and evento='ENCAMINHADA'", new { id = draftId }));

        var replay = await service.ForwardAsync(
            actor, fx.ProtocoloId, null, destination, null, null, idempotencyKey,
            null, null, null, CancellationToken.None, draftId);
        Assert.True(replay.Success, replay.Message);
        Assert.True(replay.Idempotent);
        Assert.Equal(1, await _admin.ExecuteScalarAsync<int>(
            "select count(*) from ged.protocolo_tramitacao where tenant_id=@tenant and protocolo_id=@protocolId and idempotency_key=@key",
            new { tenant = fx.TenantId, protocolId = fx.ProtocoloId, key = idempotencyKey }));
    }

    [PgGatedFact]
    public async Task Confirm_pending_item_requires_human_action_and_is_idempotent()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);
        var assist = await service.AssistAsync(new ProtocolAiAssistRequest
        {
            ProtocoloId = fx.ProtocoloId,
            TaskKind = "PENDING"
        }, CancellationToken.None);

        Assert.True(assist.Success, assist.ErrorMessage);
        Assert.NotEmpty(assist.PendingItems);
        Assert.All(assist.PendingItems, item => Assert.True(item.RequiresHumanCheck));
        Assert.Equal(0, await _admin!.ExecuteScalarAsync<int>(
            "select count(*) from ged.protocolo_pendencia where protocolo_id=@protocolId",
            new { protocolId = fx.ProtocoloId }));

        var request = new ProtocolPendingConfirmRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            PendingIndex = 0
        };
        var confirmed = await service.ConfirmPendingItemAsync(request, CancellationToken.None);
        Assert.True(confirmed.Success);
        Assert.False(confirmed.AlreadyApplied);
        Assert.NotNull(confirmed.PendingId);

        var persisted = await _admin.QuerySingleAsync<(string Status, string Origin, string Description)>(
            "select status as \"Status\", origem as \"Origin\", descricao as \"Description\" from ged.protocolo_pendencia where id=@id",
            new { id = confirmed.PendingId });
        Assert.Equal("ABERTA", persisted.Status);
        Assert.Equal("ASSISTENTE_IA", persisted.Origin);
        Assert.Equal(assist.PendingItems[0].Item, persisted.Description);

        var replay = await service.ConfirmPendingItemAsync(request, CancellationToken.None);
        Assert.True(replay.Success);
        Assert.True(replay.AlreadyApplied);
        Assert.Equal(confirmed.PendingId, replay.PendingId);
        Assert.Equal(1, await _admin.ExecuteScalarAsync<int>(
            "select count(*) from ged.protocolo_pendencia where tenant_id=@tenantId and execution_id=@executionId and item_index=0",
            new { tenantId = fx.TenantId, executionId = assist.ExecutionId }));
        Assert.Equal(1, await _admin.ExecuteScalarAsync<int>(
            "select count(*) from ged.protocolo_pendencia_historico where pendencia_id=@id and evento='CONFIRMADA'",
            new { id = confirmed.PendingId }));
    }

    [PgGatedFact]
    public async Task Review_history_lists_saved_revisions()
    {
        System.Environment.SetEnvironmentVariable("INOVAGED_AI_DETERMINISTIC", "1");
        var fx = await SeedProtocolAsync();
        var current = new TestCurrentUser { TenantId = fx.TenantId, UserId = fx.UserId };
        var service = CreateService(current);

        var assist = await service.AssistAsync(new ProtocolAiAssistRequest { ProtocoloId = fx.ProtocoloId }, CancellationToken.None);
        await service.ApplySubjectAsync(new ProtocolAiApplySubjectRequest
        {
            ProtocoloId = fx.ProtocoloId,
            ExecutionId = assist.ExecutionId,
            ConcurrencyToken = assist.ConcurrencyToken,
            Subject = "Assunto Aprovado",
            Accepted = true
        }, CancellationToken.None);

        var history = await service.GetReviewHistoryAsync(fx.ProtocoloId, 1, 10, CancellationToken.None);

        Assert.True(history.Success);
        Assert.True(history.Total >= 1);
        Assert.NotEmpty(history.Items);
        Assert.Contains(history.Items, r => r.Task == "Sugestão de assunto" && r.DecisionType == "Aceita");
    }
}
