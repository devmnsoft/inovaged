using System.Security.Claims;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Sectors;
using InovaGed.Application.Tests.Ged;
using InovaGed.Domain.Primitives;
using InovaGed.Infrastructure.Sectors;
using InovaGed.Infrastructure.Security;
using InovaGed.Web.Security;
using InovaGed.Web.Controllers;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace InovaGed.Application.Tests.Sectors;

public sealed class SectorEntryAndOpeningGateTests
{
    [Fact]
    public void Url_contract_keeps_identifiers_and_rejects_unknown_input()
    {
        var parsed = SectorEntryContract.Parse(new Dictionary<string, string?>
        {
            ["integracao"] = "parceiro-exemplo",
            ["tipoIdentificador"] = "ID",
            ["paciente"] = "000120",
            ["codigoAtendimento"] = "000088",
            ["nomeDocumento"] = "Prescrição Médica",
            ["modo"] = "consulta"
        });
        Assert.True(parsed.IsValid);
        Assert.Equal("000120", parsed.Draft!.Identifier);
        Assert.Equal("000088", parsed.Draft.EncounterCode);
        Assert.Equal("Prescrição Médica", parsed.Draft.DocumentName);

        var cpf = SectorEntryContract.Parse(new Dictionary<string, string?>
        {
            ["integracao"] = "parceiro-exemplo",
            ["tipoIdentificador"] = "CPF",
            ["paciente"] = "529.982.247-25",
            ["codigoAtendimento"] = "000088"
        });
        Assert.Equal("52998224725", cpf.Draft!.Identifier);
        Assert.Equal("consulta", cpf.Draft.Mode);
        Assert.False(SectorEntryContract.Parse(new Dictionary<string, string?>
        {
            ["integracao"] = "parceiro-exemplo",
            ["tipoIdentificador"] = "CPF",
            ["paciente"] = "111.111.111-11",
            ["codigoAtendimento"] = "000088"
        }).IsValid);
        Assert.False(SectorEntryContract.Parse(new Dictionary<string, string?>
        {
            ["integracao"] = "parceiro-exemplo",
            ["tipoIdentificador"] = "ID",
            ["paciente"] = "000120",
            ["codigoAtendimento"] = "000088",
            ["extra"] = "1"
        }).IsValid);
        Assert.False(SectorEntryContract.Parse(new Dictionary<string, string?>
        {
            ["integracao"] = "parceiro-exemplo",
            ["tipoIdentificador"] = "OUTRO",
            ["paciente"] = "000120",
            ["codigoAtendimento"] = "000088"
        }).IsValid);

        var ticket = Guid.NewGuid();
        var path = "/DemaisSetores/Continuar/" + ticket.ToString("D");
        Assert.True(SectorEntryContract.IsContinuePath(path));
        Assert.False(SectorEntryContract.IsContinuePath(path + "?paciente=000120"));
        Assert.False(SectorEntryContract.IsContinuePath("https://evil.example"));
        var hospital = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRoles.Hospital)], "test"));
        Assert.True(AppStartRouteResolver.IsAllowedReturnUrlForUser(hospital, path));
        Assert.False(AppStartRouteResolver.IsAllowedReturnUrlForUser(hospital, path + "?paciente=000120"));
        Assert.False(SectorOpeningLink.Exposes(path, "52998224725"));
    }

    [LocalPgFact]
    public async Task Entry_resolves_without_guessing_and_issues_a_consistent_pair()
    {
        var name = "ig_entrada_" + Guid.NewGuid().ToString("N");
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
            var otherTenant = Guid.NewGuid();
            var patient = Guid.NewGuid();
            var otherPatient = Guid.NewGuid();
            var cpfPatient = Guid.NewGuid();
            var encounter = Guid.NewGuid();
            var otherEncounter = Guid.NewGuid();
            var document = Guid.NewGuid();
            var secondDocument = Guid.NewGuid();
            var otherDocument = Guid.NewGuid();
            var version = Guid.NewGuid();
            var secondVersion = Guid.NewGuid();
            var otherVersion = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.sector_patient (id, tenant_id, source_system, source_unit, external_code, display_name, identifier_kind, confirmed_at)
values
 (@patient, @tenant, 'MV', '1', '000120', 'Pessoa sintética', 'ID', now()),
 (@cpfPatient, @tenant, 'MV', '1', '52998224725', 'Pessoa sintética', 'CPF', now()),
 (@otherPatient, @tenant, 'MV', '2', '000120', 'Outra unidade', 'ID', now());
insert into ged.sector_encounter (id, tenant_id, patient_id, external_code, confirmed_at) values
 (@encounter, @tenant, @patient, '000088', now()),
 (@otherEncounter, @tenant, @otherPatient, '000088', now());
insert into ged.sector_document_type (tenant_id, catalog_code, name, active) values
 (@tenant, 'DS23', 'Prescrição Médica', true),
 (@tenant, 'DS08', 'Evolução', true),
 (@tenant, 'DS30', 'Inativo', false);
insert into ged.sector_document_synonym (tenant_id, catalog_code, synonym) values
 (@tenant, 'DS23', 'Receituário');
insert into ged.document (tenant_id, id) values
 (@tenant, @document), (@tenant, @secondDocument), (@tenant, @otherDocument);
insert into ged.sector_document_link (id, tenant_id, patient_id, encounter_id, document_id, version_id, catalog_code) values
 (gen_random_uuid(), @tenant, @patient, @encounter, @document, @version, 'DS23'),
 (gen_random_uuid(), @tenant, @patient, @encounter, @secondDocument, @secondVersion, 'DS23'),
 (gen_random_uuid(), @tenant, @patient, @encounter, @otherDocument, @otherVersion, 'DS08');
""", new { tenant, patient, cpfPatient, otherPatient, encounter, otherEncounter, document, secondDocument, otherDocument, version, secondVersion, otherVersion });

            var byId = await SectorIdentityStore.FindAsync(db, tenant, "MV", "1", "ID", "000120", "000088", CancellationToken.None);
            var byCpfBefore = await SectorIdentityStore.FindAsync(db, tenant, "MV", "1", "CPF", "52998224725", "000088", CancellationToken.None);
            Assert.NotEqual(byId!.PatientId, byCpfBefore!.PatientId);
            await SectorIdentityStore.LinkAsync(db, tenant, patient, "MV", "1", "CPF", "52998224725", CancellationToken.None);
            var byCpf = await SectorIdentityStore.FindAsync(db, tenant, "MV", "1", "CPF", "52998224725", "000088", CancellationToken.None);
            Assert.Equal(patient, byCpf!.PatientId);
            Assert.Equal(encounter, byCpf.EncounterId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => SectorIdentityStore.LinkAsync(db, tenant, otherPatient, "MV", "1", "CPF", "52998224725", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => SectorIdentityStore.LinkAsync(db, otherTenant, patient, "MV", "1", "ID", "new-alias", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => SectorIdentityStore.LinkAsync(db, tenant, cpfPatient, "MV", "1", "ID", "new-alias", CancellationToken.None));
            Assert.Equal(patient, (await SectorIdentityStore.FindAsync(db, tenant, "MV", "1", "CPF", "52998224725", "000088", CancellationToken.None))!.PatientId);
            var otherUnit = await SectorIdentityStore.FindAsync(db, tenant, "MV", "2", "ID", "000120", "000088", CancellationToken.None);
            Assert.Equal(otherPatient, otherUnit!.PatientId);
            Assert.NotEqual(encounter, otherUnit.EncounterId);
            var missing = await SectorIdentityStore.FindAsync(db, tenant, "MV", "1", "ID", "120", "000088", CancellationToken.None);
            Assert.Null(missing);
            var foreign = await SectorIdentityStore.FindAsync(db, tenant, "MV", "1", "ID", "000120", "999999", CancellationToken.None);
            Assert.Equal(patient, foreign!.PatientId);
            Assert.Null(foreign.EncounterId);

            var sectors = new Sectors();
            var audit = new Audit();
            var service = new SectorOpeningService(new Factory(builder.ConnectionString), sectors, audit, NullLogger<SectorOpeningService>.Instance, new AbacAuthorizationService(new Factory(builder.ConnectionString)));
            Assert.Null(await service.RegisterIntegrationAsync(new SectorIntegrationRegistration(tenant, Guid.NewGuid(), "parceiro-exemplo", "segredo-de-teste-123", "MV", "1", ["https://parceiro.exemplo"], ["DS23"], true, false, 15), CancellationToken.None));

            var opened = await service.OpenAsync(Request("ID", "000120", "000088", "Receituário", null, null, "consulta"), CancellationToken.None);
            Assert.Equal(0, sectors.Calls);
            Assert.True(opened.Accepted);
            Assert.Contains("não foi consultada", opened.Message, StringComparison.Ordinal);
            Assert.Equal(SectorOpeningCodes.Several, opened.Code);
            Assert.NotNull(opened.PageUrl);
            Assert.NotEqual(opened.PageUrl, opened.FrameUrl);
            Assert.False(SectorOpeningLink.Exposes(opened.PageUrl!, "000120"));
            var pair = await db.QuerySingleAsync<(int Codes, int Correlations)>("""
select count(distinct code_hash) as Codes, count(distinct correlation_id) as Correlations
from ged.sector_open_code
""");
            Assert.Equal(2, pair.Codes);
            Assert.Equal(1, pair.Correlations);
            Assert.Equal(0, await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_open_code where document_id is not null"));

            var cpfOpen = await service.OpenAsync(Request("CPF", "529.982.247-25", "000088", null, "DS23", null, "consulta"), CancellationToken.None);
            Assert.Equal(0, sectors.Calls);
            Assert.Equal(SectorOpeningCodes.Several, cpfOpen.Code);

            var noPatient = await service.OpenAsync(Request("CPF", "390.533.447-05", "000088", null, "DS23", null, "consulta"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.PatientMissing, noPatient.Code);
            Assert.Null(noPatient.PageUrl);
            Assert.Equal(1, sectors.Calls);

            sectors.Next = new ClinicalSourceResult(ClinicalSourceStatus.Unavailable, "A origem clínica não respondeu.", null, []);
            var unconfirmed = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.sector_patient (id, tenant_id, source_system, source_unit, external_code, display_name, identifier_kind)
values (@id, @tenant, 'MV', '1', '000121', 'Sem confirmação', 'ID')
""", new { id = unconfirmed, tenant });
            var beforePatients = await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_patient");
            var unavailable = await service.OpenAsync(Request("ID", "000121", "000088", null, "DS23", null, "consulta"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.Unavailable, unavailable.Code);
            Assert.Equal(beforePatients, await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_patient"));
            Assert.Null(await db.ExecuteScalarAsync<Guid?>("select id from ged.sector_encounter where patient_id=@id", new { id = unconfirmed }));

            var named = await service.OpenAsync(Request("ID", "000120", "000088", "Prescrição Médica", null, null, "consulta"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.Several, named.Code);
            await db.ExecuteAsync("insert into ged.sector_document_type (tenant_id, catalog_code, name, active) values (@tenant, 'DS24', 'Prescrição Médica', true)", new { tenant });
            var ambiguousNow = await service.OpenAsync(Request("ID", "000120", "000088", "Prescrição Médica", null, null, "documento"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.Ambiguous, ambiguousNow.Code);
            Assert.Null(ambiguousNow.PageUrl);
            var contradictory = await service.OpenAsync(Request("ID", "000120", "000088", "Evolução", "DS23", null, "consulta"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.Contradictory, contradictory.Code);
            var inactive = await service.OpenAsync(Request("ID", "000120", "000088", "Inativo", null, null, "consulta"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.Invalid, inactive.Code);

            var locked = await service.OpenAsync(Request("ID", "000120", "000088", null, null, version, "consulta"), CancellationToken.None);
            Assert.Equal("documento", locked.Scope);
            Assert.True(await db.ExecuteScalarAsync<bool>("""
select bool_and(document_id is not null and version_id=@version and scope_kind='documento')
from ged.sector_open_code where correlation_id=@correlation
""", new { version, correlation = locked.CorrelationId }));
            var denied = await service.OpenAsync(Request("ID", "000120", "000088", null, null, otherVersion, "consulta"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.Denied, denied.Code);
            Assert.Null(denied.PageUrl);

            sectors.Next = new ClinicalSourceResult(ClinicalSourceStatus.Found, "encontrado", new ExternalPatient("MV", "1", "000120", "Pessoa sintética"), []);
            var divergent = await service.OpenAsync(Request("ID", "000120", "999999", null, "DS23", null, "consulta"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.EncounterMismatch, divergent.Code);
            Assert.Equal(otherPatient, await db.ExecuteScalarAsync<Guid>("select patient_id from ged.sector_encounter where id=@otherEncounter", new { otherEncounter }));

            var repeated = await service.OpenAsync(Request("ID", "000120", "000088", null, "DS23", null, "consulta", "chave-1"), CancellationToken.None);
            var again = await service.OpenAsync(Request("ID", "000120", "000088", null, "DS23", null, "consulta", "chave-1"), CancellationToken.None);
            Assert.NotNull(repeated.PageUrl);
            Assert.Equal(SectorOpeningCodes.Repeated, again.Code);
            Assert.Null(again.PageUrl);
            Assert.Equal(repeated.CorrelationId, again.CorrelationId);
            Assert.Equal(2, await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_open_code where correlation_id=@id", new { id = repeated.CorrelationId }));

            audit.Throw = true;
            var audited = await service.OpenAsync(Request("ID", "000120", "000088", null, "DS23", null, "consulta", "chave-2"), CancellationToken.None);
            audit.Throw = false;
            Assert.NotNull(audited.PageUrl);
            Assert.Equal(2, await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_open_code where correlation_id=@id", new { id = audited.CorrelationId }));

            var integrationId = await db.ExecuteScalarAsync<Guid>("select id from ged.sector_integration where client_id='parceiro-exemplo' and tenant_id=@tenant", new { tenant });
            await db.ExecuteAsync("update ged.sector_patient set confirmed_at=null where id=@patient", new { patient });
            sectors.BeforeFind = () => db.ExecuteAsync("update ged.sector_integration set catalog_codes='{DS08}' where id=@integrationId", new { integrationId });
            var raced = await service.OpenAsync(Request("ID", "000120", "000088", null, "DS23", null, "consulta", "race"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.Denied, raced.Code);
            Assert.Null(raced.PageUrl);
            Assert.Equal(0, await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_open_code where correlation_id=@correlation", new { correlation = raced.CorrelationId }));
            sectors.BeforeFind = null;
            await db.ExecuteAsync("update ged.sector_integration set catalog_codes='{DS23}' where id=@integrationId", new { integrationId });
            await db.ExecuteAsync("update ged.sector_patient set confirmed_at=now() where id=@patient", new { patient });
            for (var i = 0; i < 30; i++)
            {
                await db.ExecuteAsync("""
insert into ged.sector_open_request (id, tenant_id, integration_id, idempotency_key, correlation_id, page_code_hash, frame_code_hash, created_at)
values (gen_random_uuid(), @tenant, @integrationId, @key, @correlation, repeat('a', 64), repeat('b', 64), now())
""", new { tenant, integrationId, key = "limite-" + i, correlation = "c" + i });
            }
            var limited = await service.OpenAsync(Request("ID", "000120", "000088", null, "DS23", null, "consulta", "chave-3"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.RateLimited, limited.Code);
            Assert.Null(limited.PageUrl);

            await db.ExecuteAsync("""
insert into ged.sector_integration
 (id, tenant_id, client_id, secret_hash, source_system, source_unit, origins, active)
values (gen_random_uuid(), @otherTenant, 'parceiro-exemplo', repeat('c', 64), 'MV', '1', '{https://parceiro.exemplo}', true)
""", new { otherTenant });
            var duplicate = await service.OpenAsync(Request("ID", "000120", "000088", null, "DS23", null, "consulta", "chave-4"), CancellationToken.None);
            Assert.Equal(SectorOpeningCodes.Unauthorized, duplicate.Code);

            await db.ExecuteAsync("delete from ged.sector_integration where tenant_id=@otherTenant", new { otherTenant });
            await db.ExecuteAsync("update ged.sector_open_request set created_at = now() - interval '2 minutes'");
            var issued = await service.CreateEntryAsync(new SectorEntryDraft("parceiro-exemplo", "ID", "000120", "000088", "Receituário", null, null, null, "consulta"), CancellationToken.None);
            Assert.True(issued.Accepted);
            var user = Guid.NewGuid();
            var role = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.app_user(id, tenant_id) values (@user, @tenant);
insert into ged.role(id, tenant_id) values (@role, @tenant);
insert into ged.app_role(id, tenant_id) values (@role, @tenant);
insert into ged.user_role(user_id, role_id) values (@user, @role);
insert into ged.permission(code) values ('Documents.View');
insert into ged.role_permission(tenant_id, role_id, permission_code) values (@tenant, @role, 'Documents.View');
-- The second document has a visible type but an ACL that excludes this user.
insert into ged.document_acl(document_id, user_id, can_read, can_write) values (@secondDocument, @excluded, true, false);
""", new { user, role, tenant, secondDocument, excluded = Guid.NewGuid() });
            var continued = await service.ContinueEntryAsync(issued.TicketId!.Value, tenant, user, true, CancellationToken.None);
            Assert.True(continued.Redirect);
            Assert.StartsWith("/DemaisSetores/Abrir/", continued.PageUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("000120", continued.PageUrl, StringComparison.Ordinal);
            var userSession = await service.ConsumeAsync(continued.PageUrl!.Split('/').Last(), CancellationToken.None);
            Assert.NotNull(userSession);
            var access = await service.ReadSessionAsync(userSession!.CookieValue, userSession.PublicId, CancellationToken.None);
            Assert.Equal(user, access!.AuthorizingUserId);
            Assert.Equal("", access.Origin);
            Assert.Contains(document, access.AuthorizedDocumentIds!);
            Assert.DoesNotContain(secondDocument, access.AuthorizedDocumentIds!);
            Assert.Equal(user, audit.LastUser);
            var permitted = await db.QueryAsync<Guid>($"select l.document_id from ged.sector_document_link l where true {SectorSessionScope.LinkFilter}", SectorSessionScope.Args(access));
            Assert.Equal(new[] { document }, permitted);
            var deniedTicket = await service.CreateEntryAsync(new SectorEntryDraft("parceiro-exemplo", "ID", "000120", "000088", null, "DS23", secondDocument, secondVersion, "documento"), CancellationToken.None);
            var deniedByAcl = await service.ContinueEntryAsync(deniedTicket.TicketId!.Value, tenant, user, true, CancellationToken.None);
            Assert.False(deniedByAcl.Redirect);
            Assert.Null(deniedByAcl.PageUrl);
            foreach (var identity in new[]
            {
                new SessionUser(false, tenant, user),
                new SessionUser(true, tenant, Guid.NewGuid()),
                new SessionUser(true, otherTenant, user)
            })
            {
                var controller = new DemaisSetoresController(sectors, service, null!, null!, identity, new Factory(builder.ConnectionString), null!, new AbacAuthorizationService(new Factory(builder.ConnectionString)));
                var http = new DefaultHttpContext();
                http.User = new ClaimsPrincipal(identity.IsAuthenticated ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, identity.UserId.ToString())], "test") : new ClaimsIdentity());
                http.Request.Headers.Cookie = SectorOpeningService.CookieNameFor(userSession.PublicId) + "=" + userSession.CookieValue;
                controller.ControllerContext = new ControllerContext { HttpContext = http };
                var response = Assert.IsType<ContentResult>(await controller.ConsultaIntegrada(userSession.PublicId, CancellationToken.None));
                Assert.Equal(401, http.Response.StatusCode);
                Assert.DoesNotContain("Paciente sintético", response.Content ?? "");
            }
            await db.ExecuteAsync("update ged.app_user set is_active=false where id=@user", new { user });
            Assert.Null(await service.ReadSessionAsync(userSession.CookieValue, userSession.PublicId, CancellationToken.None));
            await db.ExecuteAsync("update ged.app_user set is_active=true where id=@user", new { user });
            sectors.EnterAllowed = false;
            Assert.Null(await service.ReadSessionAsync(userSession.CookieValue, userSession.PublicId, CancellationToken.None));
            sectors.EnterAllowed = true;
            await db.ExecuteAsync("delete from ged.role_permission where role_id=@role", new { role });
            var revoked = await service.ReadSessionAsync(userSession.CookieValue, userSession.PublicId, CancellationToken.None);
            Assert.Empty(revoked!.AuthorizedDocumentIds!);
            Assert.Empty(await db.QueryAsync<Guid>($"select l.document_id from ged.sector_document_link l where true {SectorSessionScope.LinkFilter}", SectorSessionScope.Args(revoked)));
            var consumed = await service.ContinueEntryAsync(issued.TicketId.Value, tenant, Guid.NewGuid(), true, CancellationToken.None);
            Assert.False(consumed.Redirect);
            Assert.Equal("Este link não pode ser usado.", consumed.Message);
            var wrongTenant = await service.CreateEntryAsync(new SectorEntryDraft("parceiro-exemplo", "ID", "000120", "000088", null, "DS23", null, null, "consulta"), CancellationToken.None);
            var deniedTenant = await service.ContinueEntryAsync(wrongTenant.TicketId!.Value, otherTenant, Guid.NewGuid(), true, CancellationToken.None);
            Assert.Equal("A integração não permite consultar este documento.", deniedTenant.Message);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Module_migration_installs_and_repeats_on_empty_postgres()
    {
        var name = "ig_mig28_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await control.ExecuteAsync($"create database {name}");
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
            await using var db = new NpgsqlConnection(builder.ConnectionString);
            await db.OpenAsync();
            await db.ExecuteAsync("create schema if not exists ged");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "migrations"));
            foreach (var file in new[] { "2026_10_25_demais_setores.sql", "2026_10_26_demais_setores_acesso.sql", "2026_10_27_demais_setores_sessao.sql", "2026_10_28_demais_setores_entrada.sql", "2026_10_29_demais_setores_user_session.sql" })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(root, file));
                if (file == "2026_10_29_demais_setores_user_session.sql")
                    await db.ExecuteAsync("""
insert into ged.sector_open_session
  (id,tenant_id,integration_id,cookie_hash,public_id,patient_id,encounter_id,mode,origin,correlation_id,expires_at)
values (gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),repeat('a',64),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),'consulta','https://partner.test','legacy',now()+interval '10 minutes');
insert into ged.sector_open_code
  (id,tenant_id,integration_id,code_hash,patient_id,encounter_id,mode,origin,correlation_id,expires_at)
values (gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),repeat('a',64),gen_random_uuid(),gen_random_uuid(),'consulta','https://partner.test','legacy',now()+interval '10 minutes');
""");
                await db.ExecuteAsync(sql);
                if (file == "2026_10_29_demais_setores_user_session.sql")
                {
                    Assert.True(await db.ExecuteScalarAsync<bool>("select revoked_at is not null from ged.sector_open_session where correlation_id='legacy'"));
                    Assert.True(await db.ExecuteScalarAsync<bool>("select consumed_at is not null from ged.sector_open_code where correlation_id='legacy'"));
                    await db.ExecuteAsync("""
insert into ged.sector_open_session
  (id,tenant_id,integration_id,cookie_hash,public_id,patient_id,encounter_id,mode,origin,correlation_id,expires_at)
values (gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),repeat('b',64),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),'consulta','https://partner.test','new-backend',now()+interval '10 minutes');
""");
                }
                await db.ExecuteAsync(sql);
                if (file == "2026_10_29_demais_setores_user_session.sql")
                    Assert.True(await db.ExecuteScalarAsync<bool>("select revoked_at is null from ged.sector_open_session where correlation_id='new-backend'"));
            }
            Assert.True(await db.ExecuteScalarAsync<bool>("select exists(select 1 from information_schema.columns where table_schema='ged' and table_name='sector_patient' and column_name='confirmed_at')"));
            Assert.True(await db.ExecuteScalarAsync<bool>("select to_regclass('ged.sector_entry_ticket') is not null"));
            Assert.True(await db.ExecuteScalarAsync<bool>("select to_regclass('ged.sector_open_request') is not null"));
            Assert.True(await db.ExecuteScalarAsync<bool>("select to_regclass('ged.sector_patient_alias') is not null"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    private static SectorOpeningRequest Request(string kind, string patient, string encounter, string? name, string? code, Guid? versionId, string mode, string? idempotency = null)
        => new("parceiro-exemplo", "segredo-de-teste-123", kind, patient, "MV", "1", encounter, name, code, null, versionId, mode, "https://parceiro.exemplo", idempotency);

    private const string Schema = """
create schema if not exists ged;
create table ged.sector_patient (
    id uuid primary key, tenant_id uuid not null, source_system text not null, source_unit text not null default '',
    external_code text not null, display_name text not null, identifier_kind text not null default 'ID',
    confirmed_at timestamptz, canonical_patient_id uuid, updated_at timestamptz not null default now(),
    unique (tenant_id, source_system, source_unit, identifier_kind, external_code));
create table ged.sector_encounter (
    id uuid primary key, tenant_id uuid not null, patient_id uuid not null, external_code text not null,
    confirmed_at timestamptz, unique (tenant_id, patient_id, external_code));
create table ged.sector_patient_alias (
    id uuid primary key, tenant_id uuid not null, patient_id uuid not null, source_system text not null,
    source_unit text not null default '', identifier_kind text not null, external_code text not null,
    confirmed_at timestamptz not null default now(),
    unique (tenant_id, source_system, source_unit, identifier_kind, external_code));
create table ged.sector_integration (
    id uuid primary key, tenant_id uuid not null, client_id text not null, secret_hash char(64) not null,
    source_system text not null, source_unit text not null default '', origins text[] not null default '{}',
    catalog_codes text[] not null default '{}', allow_encounter_list boolean not null default false,
    allow_encounter_switch boolean not null default false, max_minutes integer not null default 15, active boolean not null default true,
    unique (tenant_id, client_id));
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
    created_at timestamptz not null default now(), unique (integration_id, idempotency_key));
create table ged.sector_entry_ticket (
    id uuid primary key, integration_id uuid not null, tenant_id uuid not null, identifier_kind text not null,
    identifier_value text not null, encounter_code text not null, document_name text, document_code text,
    document_id uuid, version_id uuid, mode text not null, created_at timestamptz not null default now(),
    expires_at timestamptz not null, consumed_at timestamptz);
create table ged.sector_document_type (
    tenant_id uuid not null, catalog_code text not null, name text not null, active boolean not null default true,
    synonyms text[] not null default '{}', primary key (tenant_id, catalog_code));
create table ged.sector_document_synonym (
    tenant_id uuid not null, catalog_code text not null, synonym text not null, unique (tenant_id, synonym));
create table ged.document (tenant_id uuid not null, id uuid not null, reg_status char(1) not null default 'A', is_confidential boolean not null default false, primary key (tenant_id, id));
create table ged.app_user(id uuid primary key, tenant_id uuid not null, is_active boolean default true, is_locked boolean default false, reg_status char(1) default 'A');
create table ged.role(id uuid primary key, tenant_id uuid);
create table ged.app_role(id uuid primary key, tenant_id uuid);
create table ged.user_role(user_id uuid, role_id uuid);
create table ged.permission(code text primary key, reg_status char(1) default 'A');
create table ged.role_permission(tenant_id uuid, role_id uuid, permission_code text, reg_status char(1) default 'A');
create table ged.document_acl(document_id uuid, user_id uuid, role_id uuid, can_read boolean, can_write boolean);
create table ged.sector_document_link (
    id uuid primary key, tenant_id uuid not null, patient_id uuid not null, encounter_id uuid not null,
    document_id uuid not null, version_id uuid not null, catalog_code text not null, reg_status char(1) not null default 'A');
""";

    private sealed class Factory : IDbConnectionFactory
    {
        private readonly string _connectionString;
        public Factory(string connectionString) => _connectionString = connectionString;
        public System.Data.IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
        public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            return connection;
        }
    }

    private sealed record SessionUser(bool IsAuthenticated, Guid TenantId, Guid UserId) : ICurrentUser
    {
        public string Email => "synthetic@example.test";
        public IReadOnlyList<string> Roles => [];
    }

    private sealed class Audit : IAuditWriter
    {
        public bool Throw;
        public Guid? LastUser;
        public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct)
        {
            if (Throw) throw new InvalidOperationException("auditoria");
            LastUser = command.UserId;
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data, CancellationToken ct)
            => WriteAsync(new AuditWriteCommand(tenantId, userId, action, entityName, entityId, summary, ipAddress, userAgent, null, data), ct);
    }

    private sealed class Sectors : IDemaisSetoresService
    {
        public int Calls;
        public Func<Task>? BeforeFind;
        public bool EnterAllowed = true;
        public ClinicalSourceResult Next = new(ClinicalSourceStatus.NotFound, "Paciente não localizado.", null, []);
        public Task<bool> CanEnterAsync(Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct) => Task.FromResult(EnterAllowed);
        public Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, bool isFullAdmin, string permission, CancellationToken ct) => Task.FromResult(true);
        public Task EnsureTenantAsync(Guid tenantId, CancellationToken ct) => Task.CompletedTask;
        public Task<ClinicalSourceResult> FindPatientAsync(Guid tenantId, string? externalCode, string? sourceSystem, string? sourceUnit, CancellationToken ct)
            => FindPatientAsync(tenantId, externalCode, sourceSystem, sourceUnit, "ID", ct);
        public async Task<ClinicalSourceResult> FindPatientAsync(Guid tenantId, string? externalCode, string? sourceSystem, string? sourceUnit, string identifierKind, CancellationToken ct)
        {
            Calls++;
            if (BeforeFind is not null) await BeforeFind();
            return Next;
        }
        public Task<SectorGrantView?> ReadGrantAsync(string token, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SectorUploadTicket> PrepareUploadAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid patientId, Guid encounterId, string catalogCode, string fileName, long totalBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> RevokeAccessAsync(Guid tenantId, Guid actorId, bool isFullAdmin, Guid userId, string sectorCode, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> VisibleCatalogCodesAsync(Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["DS23"]);
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
        public Task<SectorEmbedResult> CreateEmbedAsync(SectorEmbedRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> GrantIsActiveAsync(string token, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
        public Task RevokeEmbedAsync(Guid tenantId, Guid grantId, Guid actorId, CancellationToken ct) => throw new NotSupportedException();
    }
}
