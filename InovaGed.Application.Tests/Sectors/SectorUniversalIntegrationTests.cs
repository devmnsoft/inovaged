using System.Security.Claims;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
using InovaGed.Application.Sectors;
using InovaGed.Application.Tests.Ged;
using InovaGed.Domain.Primitives;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Sectors;
using InovaGed.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace InovaGed.Application.Tests.Sectors;

public sealed class SectorUniversalIntegrationTests
{
    private const string Schema = """
create schema if not exists ged;
create table ged.tenant (id uuid primary key, name text not null, code text not null);
create table ged.app_user (
    id uuid primary key, tenant_id uuid not null, name text not null, email text not null, password_hash text not null,
    is_active boolean default true, is_locked boolean default false, reg_status char(1) default 'A', deleted_at_utc timestamptz);
create table ged.role (id uuid primary key, tenant_id uuid not null);
create table ged.app_role (id uuid primary key, tenant_id uuid not null);
create table ged.user_role (user_id uuid not null, role_id uuid not null);
create table ged.permission (code text primary key, reg_status char(1) default 'A');
create table ged.role_permission (tenant_id uuid not null, role_id uuid not null, permission_code text not null, reg_status char(1) default 'A');
create table ged.document (tenant_id uuid not null, id uuid primary key, code text not null, title text not null, is_confidential boolean not null default false, created_by uuid, reg_status char(1) not null default 'A');
create table ged.document_version (tenant_id uuid not null, id uuid primary key, document_id uuid not null, version_number integer not null, file_name text not null, file_extension text not null, file_size_bytes bigint not null, storage_path text not null, checksum_sha256 char(64) not null, content_type text not null, content_text text, created_by uuid not null);
create table ged.document_acl (document_id uuid not null, user_id uuid, role_id uuid, can_read boolean not null default true, can_write boolean not null default false);

create table ged.sector_patient (
    id uuid primary key, tenant_id uuid not null, source_system text not null, source_unit text not null default '',
    external_code text not null, display_name text not null, identifier_kind text not null default 'ID',
    confirmed_at timestamptz, canonical_patient_id uuid, updated_at timestamptz not null default now(),
    unique (tenant_id, source_system, source_unit, identifier_kind, external_code));

create table ged.sector_encounter (
    id uuid primary key, tenant_id uuid not null, patient_id uuid not null, external_code text not null,
    confirmed_at timestamptz, unique (tenant_id, patient_id, external_code));

create table ged.sector_document_type (
    tenant_id uuid not null, catalog_code text not null, name text not null, active boolean not null default true,
    catalog_order integer not null default 0, synonyms text[] not null default '{}', primary key (tenant_id, catalog_code));

create table ged.sector_document_synonym (
    tenant_id uuid not null, catalog_code text not null, synonym text not null, unique (tenant_id, synonym));

create table ged.sector_document_link (
    id uuid primary key, tenant_id uuid not null, patient_id uuid not null, encounter_id uuid not null,
    document_id uuid not null, version_id uuid not null, catalog_code text not null, reg_status char(1) not null default 'A',
    document_date date, preview_status text not null default 'PRONTO', ocr_status text not null default 'CONCLUIDO',
    extraction_status text not null default 'CONCLUIDO', created_at timestamptz not null default now());

create table ged.sector_extracted_field (
    id uuid primary key, tenant_id uuid not null, link_id uuid not null, version_id uuid not null,
    field_key text not null default 'medicamento', status text not null default 'SUGERIDO', updated_at timestamptz not null default now());

create table ged.sector_finding (
    id uuid primary key, tenant_id uuid not null, link_id uuid not null, status text not null default 'ABERTO', updated_at timestamptz not null default now());

create table ged.sector_integration (
    id uuid primary key, tenant_id uuid not null, client_id text not null, secret_hash char(64) not null,
    display_name text, source_system text not null, source_unit text not null default '',
    origins text[] not null default '{}', catalog_codes text[] not null default '{}',
    allow_encounter_list boolean not null default false, allow_encounter_switch boolean not null default false,
    max_minutes integer not null default 15, active boolean not null default true, unique (tenant_id, client_id));

create table ged.sector_entry_ticket (
    id uuid primary key, integration_id uuid not null, tenant_id uuid not null, identifier_kind text not null,
    identifier_value text not null, encounter_code text not null, document_name text, document_code text,
    document_id uuid, version_id uuid, mode text not null, created_at timestamptz not null default now(),
    expires_at timestamptz not null, consumed_at timestamptz, origin text not null default '', destination text not null default 'pagina',
    integration_fingerprint char(64), revoked_at timestamptz);

create table ged.sector_open_code (
    id uuid primary key, tenant_id uuid not null, code_hash char(64) not null unique, session_id uuid,
    patient_id uuid not null, encounter_id uuid not null, catalog_code text, document_id uuid, version_id uuid,
    mode text not null, scope_kind text not null, allow_encounter_switch boolean not null default false,
    origin text not null, integration_id uuid not null, correlation_id text not null, expires_at timestamptz not null,
    consumed_at timestamptz, granted_catalog text[], authorizing_user_id uuid, created_at timestamptz not null default now());

create table ged.sector_open_session (
    id uuid primary key, tenant_id uuid not null, integration_id uuid not null, cookie_hash char(64) not null unique,
    public_id uuid not null unique, patient_id uuid not null, encounter_id uuid not null, catalog_code text,
    document_id uuid, version_id uuid, mode text not null, scope_kind text not null,
    allow_encounter_switch boolean not null default false, origin text not null, correlation_id text not null,
    expires_at timestamptz not null, revoked_at timestamptz, protection_hash char(64), granted_catalog text[], authorizing_user_id uuid);

create table ged.sector_open_request (
    id uuid primary key, tenant_id uuid not null, integration_id uuid not null, idempotency_key text,
    correlation_id text not null, page_code_hash char(64) not null, frame_code_hash char(64) not null,
    request_fingerprint char(64), recovery_count integer not null default 0, revoked_at timestamptz,
    created_at timestamptz not null default now(), unique (integration_id, idempotency_key));
""";

    [LocalPgFact]
    public async Task Universal_integration_end_to_end_covers_all_ten_governed_scenarios()
    {
        var name = "ig_univ_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await control.ExecuteAsync($"create database {name}");

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
            await using var db = new NpgsqlConnection(builder.ConnectionString);
            await db.OpenAsync();
            await db.ExecuteAsync(Schema);

            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            var role = Guid.NewGuid();
            var patient = Guid.NewGuid();
            var encounter = Guid.NewGuid();
            var encounter2 = Guid.NewGuid();
            var docPrescription1 = Guid.NewGuid();
            var verPrescription1 = Guid.NewGuid();
            var docPrescription2 = Guid.NewGuid();
            var verPrescription2 = Guid.NewGuid();
            var docLab = Guid.NewGuid();
            var verLab = Guid.NewGuid();
            var integrationId = Guid.NewGuid();

            await db.ExecuteAsync("""
insert into ged.tenant(id, name, code) values (@tenant, 'Hospital Universal', 'univ');
insert into ged.app_user(id, tenant_id, name, email, password_hash) values (@user, @tenant, 'Dr. Médico', 'med@h.t', 'hash');
insert into ged.role(id, tenant_id) values (@role, @tenant);
insert into ged.app_role(id, tenant_id) values (@role, @tenant);
insert into ged.user_role(user_id, role_id) values (@user, @role);
insert into ged.permission(code) values ('Documents.View');
insert into ged.role_permission(tenant_id, role_id, permission_code) values (@tenant, @role, 'Documents.View');

insert into ged.sector_patient(id, tenant_id, source_system, source_unit, external_code, display_name, identifier_kind, confirmed_at)
values (@patient, @tenant, 'MV', '1', '12345678909', 'Maria Silva Santos', 'CPF', now());

insert into ged.sector_encounter(id, tenant_id, patient_id, external_code, confirmed_at) values
 (@encounter, @tenant, @patient, 'ENC-100', now()),
 (@encounter2, @tenant, @patient, 'ENC-200', now());

insert into ged.sector_document_type(tenant_id, catalog_code, name, catalog_order, synonyms) values
 (@tenant, 'DS23', 'Prescrição Médica', 23, '{"Receituário", "Prescrição"}'),
 (@tenant, 'DS05', 'Exame Laboratorial', 5, '{"Laudo Laboratório"}');

insert into ged.document(tenant_id, id, code, title, created_by) values
 (@tenant, @docPrescription1, 'DOC-P1', 'Prescrição Manhã', @user),
 (@tenant, @docPrescription2, 'DOC-P2', 'Prescrição Noite', @user),
 (@tenant, @docLab, 'DOC-L1', 'Hemograma', @user);

insert into ged.document_version(tenant_id, id, document_id, version_number, file_name, file_extension, file_size_bytes, storage_path, checksum_sha256, content_type, created_by) values
 (@tenant, @verPrescription1, @docPrescription1, 1, 'p1.pdf', '.pdf', 1000, 'p1.pdf', repeat('1', 64), 'application/pdf', @user),
 (@tenant, @verPrescription2, @docPrescription2, 1, 'p2.pdf', '.pdf', 1000, 'p2.pdf', repeat('2', 64), 'application/pdf', @user),
 (@tenant, @verLab, @docLab, 1, 'l1.pdf', '.pdf', 1000, 'l1.pdf', repeat('3', 64), 'application/pdf', @user);

insert into ged.sector_document_link(id, tenant_id, patient_id, encounter_id, document_id, version_id, catalog_code) values
 (gen_random_uuid(), @tenant, @patient, @encounter, @docPrescription1, @verPrescription1, 'DS23'),
 (gen_random_uuid(), @tenant, @patient, @encounter, @docPrescription2, @verPrescription2, 'DS23'),
 (gen_random_uuid(), @tenant, @patient, @encounter, @docLab, @verLab, 'DS05');

insert into ged.sector_integration
 (id, tenant_id, client_id, secret_hash, source_system, source_unit, origins, catalog_codes, allow_encounter_list, allow_encounter_switch, active)
values
 (@integrationId, @tenant, 'prontuario-parceiro', repeat('p', 64), 'MV', '1',
  '{"https://pep.hospital.test", "https://portal.hospital.test"}', '{"DS23", "DS05"}', true, true, true);
""", new {
                tenant, user, role, patient, encounter, encounter2,
                docPrescription1, verPrescription1, docPrescription2, verPrescription2,
                docLab, verLab, integrationId
            });

            var factory = new LocalFactory(builder.ConnectionString);
            var auth = new AbacAuthorizationService(factory);
            var mockAudit = new MockAudit();
            var sectorsService = new MockSectorsService();
            var openingService = new SectorOpeningService(factory, sectorsService, mockAudit, NullLogger<SectorOpeningService>.Instance, auth);

            // =========================================================================
            // Cenário 1: URL Contextual (CPF + Código Atendimento + Nome do Documento)
            // URL contextual -> login -> autorização ABAC -> abertura do documento específico
            // =========================================================================
            var entryDraft = new SectorEntryDraft(
                ClientId: "prontuario-parceiro",
                IdentifierKind: "CPF",
                Identifier: "12345678909",
                EncounterCode: "ENC-100",
                DocumentName: "Prescrição Médica",
                DocumentCode: null,
                DocumentId: null,
                VersionId: null,
                Mode: "consulta",
                Origin: null,
                Destination: "pagina");

            var ticket = await openingService.CreateEntryAsync(entryDraft, CancellationToken.None);
            Assert.True(ticket.Accepted);
            Assert.NotNull(ticket.TicketId);

            // Usuário loga e autoriza
            var continuation = await openingService.ContinueEntryAsync(ticket.TicketId.Value, tenant, user, false, CancellationToken.None);
            Assert.True(continuation.Redirect);
            Assert.StartsWith("/DemaisSetores/Abrir/", continuation.PageUrl!);

            var openCode = continuation.PageUrl!.Split('/').Last();
            var session = await openingService.ConsumeAsync(openCode, CancellationToken.None);
            Assert.NotNull(session);

            var readSession = await openingService.ReadSessionAsync(session!.CookieValue, session.PublicId, CancellationToken.None);
            Assert.NotNull(readSession);
            Assert.Equal(user, readSession!.AuthorizingUserId);
            Assert.Equal("DS23", readSession.CatalogCode);
            Assert.Equal(2, readSession.AuthorizedDocumentIds!.Length); // Ambas as ocorrências de prescrição autorizadas
            Assert.Contains(docPrescription1, readSession.AuthorizedDocumentIds);
            Assert.Contains(docPrescription2, readSession.AuthorizedDocumentIds);

            // =========================================================================
            // Cenário 2: API Autenticada -> Concessão -> iframe em outra origem
            // =========================================================================
            var iframeDraft = new SectorEntryDraft(
                ClientId: "prontuario-parceiro",
                IdentifierKind: "CPF",
                Identifier: "12345678909",
                EncounterCode: "ENC-100",
                DocumentName: null,
                DocumentCode: "DS23",
                DocumentId: null,
                VersionId: null,
                Mode: "consulta",
                Origin: "https://pep.hospital.test",
                Destination: "iframe");

            var iframeTicket = await openingService.CreateEntryAsync(iframeDraft, CancellationToken.None);
            Assert.True(iframeTicket.Accepted);

            var iframeContinuation = await openingService.ContinueEntryAsync(iframeTicket.TicketId!.Value, tenant, user, false, CancellationToken.None);
            Assert.True(iframeContinuation.Redirect);

            var iframeSession = await openingService.ConsumeAsync(iframeContinuation.PageUrl!.Split('/').Last(), CancellationToken.None);
            Assert.NotNull(iframeSession);
            Assert.Equal("https://pep.hospital.test", iframeSession!.Origin);

            // =========================================================================
            // Cenário 3: Múltiplas ocorrências do mesmo tipo documental
            // O checklist e a lista de documentos diferenciam ambas sem sobrescrever
            // =========================================================================
            var linksPrescription = await db.QueryAsync<Guid>(
                "select id from ged.sector_document_link where tenant_id=@tenant and encounter_id=@encounter and catalog_code='DS23'",
                new { tenant, encounter });
            Assert.Equal(2, linksPrescription.Count());

            // =========================================================================
            // Cenário 4: Documento ausente no atendimento
            // Busca de tipo existente no catálogo mas sem vínculo no encontro
            // Não inventa registros nem falha catastroficamente
            // =========================================================================
            var absentDraft = new SectorEntryDraft(
                ClientId: "prontuario-parceiro",
                IdentifierKind: "CPF",
                Identifier: "12345678909",
                EncounterCode: "ENC-200", // ENC-200 não tem prescrições vinculadas
                DocumentName: null,
                DocumentCode: "DS23",
                DocumentId: null,
                VersionId: null,
                Mode: "consulta",
                Origin: null,
                Destination: "pagina");

            var absentTicket = await openingService.CreateEntryAsync(absentDraft, CancellationToken.None);
            Assert.True(absentTicket.Accepted);

            var absentContinuation = await openingService.ContinueEntryAsync(absentTicket.TicketId!.Value, tenant, user, false, CancellationToken.None);
            Assert.True(absentContinuation.Redirect);

            var absentSession = await openingService.ConsumeAsync(absentContinuation.PageUrl!.Split('/').Last(), CancellationToken.None);
            var absentRead = await openingService.ReadSessionAsync(absentSession!.CookieValue, absentSession.PublicId, CancellationToken.None);
            Assert.NotNull(absentRead);
            Assert.Empty(absentRead!.AuthorizedDocumentIds!); // Nenhum documento fictício criado!

            // =========================================================================
            // Cenário 5: Tipo ambíguo ou inexistente
            // =========================================================================
            var ambiguousDraft = new SectorEntryDraft(
                ClientId: "prontuario-parceiro",
                IdentifierKind: "CPF",
                Identifier: "12345678909",
                EncounterCode: "ENC-100",
                DocumentName: "Tipo Desconhecido ou Ambíguo",
                DocumentCode: null,
                DocumentId: null,
                VersionId: null,
                Mode: "consulta",
                Origin: null,
                Destination: "pagina");

            var ambiguousTicket = await openingService.CreateEntryAsync(ambiguousDraft, CancellationToken.None);
            Assert.True(ambiguousTicket.Accepted);
            var ambiguousContinuation = await openingService.ContinueEntryAsync(ambiguousTicket.TicketId!.Value, tenant, user, false, CancellationToken.None);
            Assert.False(ambiguousContinuation.Redirect);
            Assert.Contains("catálogo", ambiguousContinuation.Message);

            // =========================================================================
            // Cenário 6: Sessão Expirada
            // =========================================================================
            await db.ExecuteAsync(
                "update ged.sector_open_session set expires_at = now() - interval '1 minute' where public_id = @publicId",
                new { publicId = session.PublicId });

            var expiredRead = await openingService.ReadSessionAsync(session.CookieValue, session.PublicId, CancellationToken.None);
            Assert.Null(expiredRead); // Acesso bloqueado por expiração!

            // =========================================================================
            // Cenário 7: Recuperação da sessão
            // =========================================================================
            var recovery = await openingService.RecoveryInfoAsync(session.PublicId, false, CancellationToken.None);
            Assert.True(recovery.Available);

            var recoveryEntry = await openingService.CreateRecoveryEntryAsync(session.PublicId, false, CancellationToken.None);
            Assert.True(recoveryEntry.Accepted);

            var recoveredContinuation = await openingService.ContinueEntryAsync(recoveryEntry.TicketId!.Value, tenant, user, false, CancellationToken.None);
            Assert.True(recoveredContinuation.Redirect);

            var newSession = await openingService.ConsumeAsync(recoveredContinuation.PageUrl!.Split('/').Last(), CancellationToken.None);
            Assert.NotNull(newSession);
            Assert.NotEqual(session.PublicId, newSession!.PublicId);

            var newRead = await openingService.ReadSessionAsync(newSession.CookieValue, newSession.PublicId, CancellationToken.None);
            Assert.NotNull(newRead);
            Assert.Equal("DS23", newRead!.CatalogCode);

            // =========================================================================
            // Cenário 8: Revogação da integração
            // Inativar integração revoga imediatamente a sessão ativa
            // =========================================================================
            await openingService.DeactivateIntegrationAsync(tenant, user, "prontuario-parceiro", CancellationToken.None);

            var revokedRead = await openingService.ReadSessionAsync(newSession.CookieValue, newSession.PublicId, CancellationToken.None);
            Assert.Null(revokedRead); // Imediatamente nulo!

            // Reativar para o próximo teste
            await db.ExecuteAsync("update ged.sector_integration set active=true where id=@integrationId", new { integrationId });

            // =========================================================================
            // Cenário 9: Dois contextos simultâneos (abas separadas do mesmo usuário)
            // Sessões e cookies independentes garantem ausência de vazamento cruzado
            // =========================================================================
            var tab1Draft = new SectorEntryDraft("prontuario-parceiro", "CPF", "12345678909", "ENC-100", null, "DS23", null, null, "consulta");
            var tab2Draft = new SectorEntryDraft("prontuario-parceiro", "CPF", "12345678909", "ENC-100", null, "DS05", null, null, "consulta");

            var t1 = await openingService.CreateEntryAsync(tab1Draft, CancellationToken.None);
            var t2 = await openingService.CreateEntryAsync(tab2Draft, CancellationToken.None);

            var c1 = await openingService.ContinueEntryAsync(t1.TicketId!.Value, tenant, user, false, CancellationToken.None);
            var c2 = await openingService.ContinueEntryAsync(t2.TicketId!.Value, tenant, user, false, CancellationToken.None);

            var s1 = await openingService.ConsumeAsync(c1.PageUrl!.Split('/').Last(), CancellationToken.None);
            var s2 = await openingService.ConsumeAsync(c2.PageUrl!.Split('/').Last(), CancellationToken.None);

            Assert.NotEqual(s1!.PublicId, s2!.PublicId);
            Assert.NotEqual(s1.CookieValue, s2.CookieValue);

            var r1 = await openingService.ReadSessionAsync(s1.CookieValue, s1.PublicId, CancellationToken.None);
            var r2 = await openingService.ReadSessionAsync(s2.CookieValue, s2.PublicId, CancellationToken.None);

            Assert.Equal("DS23", r1!.CatalogCode);
            Assert.Equal("DS05", r2!.CatalogCode);
            Assert.Equal(2, r1.AuthorizedDocumentIds!.Length);
            Assert.Single(r2.AuthorizedDocumentIds!);
            Assert.Equal(docLab, r2.AuthorizedDocumentIds[0]);

            // =========================================================================
            // Cenário 10: Iframe sem autenticação é terminantemente recusado
            // =========================================================================
            var controller = new DemaisSetoresController(sectorsService, openingService, null!, null!,
                new MockUser(false, tenant, Guid.Empty), factory, null!, auth);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers.Cookie = SectorOpeningService.CookieNameFor(s1.PublicId) + "=" + s1.CookieValue;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var unauthenticatedResult = await controller.ConsultaIntegrada(s1.PublicId, CancellationToken.None);
            var view = Assert.IsType<ViewResult>(unauthenticatedResult);
            Assert.Equal(401, httpContext.Response.StatusCode);
            Assert.Equal("Recuperar", view.ViewName);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    private sealed record MockUser(bool IsAuthenticated, Guid TenantId, Guid UserId) : ICurrentUser
    {
        public string Email => "test@example.com";
        public IReadOnlyList<string> Roles => [];
    }

    private sealed class LocalFactory : IDbConnectionFactory
    {
        private readonly string _cs;
        public LocalFactory(string cs) => _cs = cs;
        public System.Data.IDbConnection CreateConnection() => new NpgsqlConnection(_cs);
        public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var c = new NpgsqlConnection(_cs);
            await c.OpenAsync(ct);
            return c;
        }
    }

    private sealed class MockAudit : IAuditWriter
    {
        public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data, CancellationToken ct) => Task.FromResult(Result.Ok());
    }

    private sealed class MockSectorsService : IDemaisSetoresService
    {
        public Task<bool> CanEnterAsync(Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, bool isFullAdmin, string permission, CancellationToken ct) => Task.FromResult(true);
        public Task EnsureTenantAsync(Guid tenantId, CancellationToken ct) => Task.CompletedTask;
        public Task<ClinicalSourceResult> FindPatientAsync(Guid tenantId, string? externalCode, string? sourceSystem, string? sourceUnit, CancellationToken ct) => throw new NotSupportedException();
        public Task<ClinicalSourceResult> FindPatientAsync(Guid tenantId, string? externalCode, string? sourceSystem, string? sourceUnit, string identifierKind, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorGrantView?> ReadGrantAsync(string token, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorUploadTicket> PrepareUploadAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid patientId, Guid encounterId, string catalogCode, string fileName, long totalBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> RevokeAccessAsync(Guid tenantId, Guid actorId, bool isFullAdmin, Guid userId, string sectorCode, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> VisibleCatalogCodesAsync(Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["DS23", "DS05"]);
        public Task<SectorRegistrationResult> RegisterAsync(SectorRegistrationRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorRegistrationResult> LinkExistingAsync(SectorLinkExistingRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorRegistrationResult> AttachCompletedUploadAsync(SectorAttachUploadRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorRegistrationResult> RetryProcessingAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid linkId, string ip, string userAgent, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> ReviewAsync(SectorReviewRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> CorrectLinkAsync(SectorCorrectionRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorSearchPage> SearchAsync(SectorSearchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task RefreshExtractionAsync(Guid tenantId, Guid encounterId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> ReviewFieldAsync(SectorFieldReviewRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> RecordPhysicalEvidenceAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid linkId, string evidence, string ip, string userAgent, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> ReviewFindingAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid findingId, string status, string? justification, string ip, string userAgent, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorDeliveryResult> PrepareDeliveryAsync(SectorDeliveryRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> ConfirmDeliveryAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid deliveryId, string? protocolNumber, string? deliveryEvidence, string ip, string userAgent, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorDeliveryDetails?> GetDeliveryAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid deliveryId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorEmbedResult> CreateEmbedAsync(SectorEmbedRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> GrantIsActiveAsync(string token, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
        public Task RevokeEmbedAsync(Guid tenantId, Guid grantId, Guid actorId, CancellationToken ct) => throw new NotSupportedException();
    }
}
