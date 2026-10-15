using Dapper;
using InovaGed.Application.Sectors;
using InovaGed.Application.Tests.Ged;
using InovaGed.Infrastructure.Sectors;
using Npgsql;

namespace InovaGed.Application.Tests.Sectors;

public sealed class SectorOpeningExchangeTests
{
    [LocalPgFact]
    public async Task Consume_is_atomic_and_scope_is_rechecked()
    {
        var name = "ig_abertura_" + Guid.NewGuid().ToString("N");
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
            var patient = Guid.NewGuid();
            var encounter = Guid.NewGuid();
            var other = Guid.NewGuid();
            var integration = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.sector_patient (id, tenant_id, source_system, source_unit, external_code, display_name, identifier_kind)
values (@patient, @tenant, 'MV', '1', '000120', 'Paciente sintético', 'ID');
insert into ged.sector_encounter (id, tenant_id, patient_id, external_code) values
 (@encounter, @tenant, @patient, 'ATD-1'),
 (@other, @tenant, @patient, 'ATD-2');
insert into ged.sector_integration
 (id, tenant_id, client_id, secret_hash, source_system, source_unit, origins, catalog_codes, allow_encounter_list, allow_encounter_switch, active)
values
 (@integration, @tenant, 'parceiro', repeat('a', 64), 'MV', '1', '{https://parceiro.exemplo}', '{DS23}', true, false, true);
insert into ged.document (tenant_id, id) values (@tenant, @documentId);
insert into ged.sector_document_type (tenant_id, catalog_code, name) values (@tenant, 'DS23', 'Prescrição');
insert into ged.sector_document_link (id, tenant_id, patient_id, encounter_id, document_id, version_id, catalog_code)
values (gen_random_uuid(), @tenant, @patient, @encounter, @documentId, @versionId, 'DS23');
""", new { tenant, patient, encounter, other, integration, documentId, versionId });

            var shared = NewCode();
            await InsertCode(db, shared, tenant, patient, encounter, integration, "atendimento", null, null, null, true);
            await using (var first = new NpgsqlConnection(builder.ConnectionString))
            await using (var second = new NpgsqlConnection(builder.ConnectionString))
            {
                await first.OpenAsync();
                await second.OpenAsync();
                var results = await Task.WhenAll(
                    SectorOpeningExchange.ConsumeAsync(first, shared, CancellationToken.None),
                    SectorOpeningExchange.ConsumeAsync(second, shared, CancellationToken.None));
                Assert.Equal(1, results.Count(item => item is not null));
            }
            var again = await SectorOpeningExchange.ConsumeAsync(db, shared, CancellationToken.None);
            Assert.Null(again);

            var cancelled = NewCode();
            await InsertCode(db, cancelled, tenant, patient, encounter, integration, "atendimento", null, null, null, false);
            await using (var locker = new NpgsqlConnection(builder.ConnectionString))
            {
                await locker.OpenAsync();
                await using var hold = await locker.BeginTransactionAsync();
                await locker.ExecuteAsync("select id from ged.sector_open_code where code_hash=@hash for update", new { hash = SectorOpeningExchange.Hash(cancelled) }, hold);
                await using var waiter = new NpgsqlConnection(builder.ConnectionString);
                await waiter.OpenAsync();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SectorOpeningExchange.ConsumeAsync(waiter, cancelled, cts.Token));
                await hold.RollbackAsync();
            }
            var cancelledAt = await db.ExecuteScalarAsync<DateTime?>("select consumed_at from ged.sector_open_code where code_hash=@hash", new { hash = SectorOpeningExchange.Hash(cancelled) });
            Assert.Null(cancelledAt);
            var afterCancel = await SectorOpeningExchange.ConsumeAsync(db, cancelled, CancellationToken.None);
            Assert.NotNull(afterCancel);

            var broken = NewCode();
            await InsertCode(db, broken, tenant, patient, encounter, integration, "quebrado", null, null, null, false);
            await Assert.ThrowsAnyAsync<Exception>(() => SectorOpeningExchange.ConsumeAsync(db, broken, CancellationToken.None));
            var brokenAt = await db.ExecuteScalarAsync<DateTime?>("select consumed_at from ged.sector_open_code where code_hash=@hash", new { hash = SectorOpeningExchange.Hash(broken) });
            Assert.Null(brokenAt);

            await db.ExecuteAsync("update ged.sector_integration set active=false where id=@integration", new { integration });
            var inactiveCode = NewCode();
            await InsertCode(db, inactiveCode, tenant, patient, encounter, integration, "atendimento", null, null, null, false);
            Assert.Null(await SectorOpeningExchange.ConsumeAsync(db, inactiveCode, CancellationToken.None));
            Assert.Null(await db.ExecuteScalarAsync<DateTime?>("select consumed_at from ged.sector_open_code where code_hash=@hash", new { hash = SectorOpeningExchange.Hash(inactiveCode) }));
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, afterCancel!.CookieValue, afterCancel.PublicId, CancellationToken.None));

            await db.ExecuteAsync("update ged.sector_integration set active=true, allow_encounter_switch=true where id=@integration", new { integration });
            var page = NewCode();
            var frame = NewCode();
            await InsertCode(db, page, tenant, patient, encounter, integration, "atendimento", null, null, null, true);
            await InsertCode(db, frame, tenant, patient, encounter, integration, "atendimento", null, null, null, true);
            var pageSession = await SectorOpeningExchange.ConsumeAsync(db, page, CancellationToken.None);
            var frameSession = await SectorOpeningExchange.ConsumeAsync(db, frame, CancellationToken.None);
            Assert.NotEqual(pageSession!.PublicId, frameSession!.PublicId);
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, pageSession.CookieValue, frameSession.PublicId, CancellationToken.None));
            var pageAccess = await SectorOpeningExchange.ReadAsync(db, pageSession.CookieValue, pageSession.PublicId, CancellationToken.None);
            Assert.True(pageAccess!.AllowEncounterSwitch);
            Assert.True(await SectorOpeningExchange.AuthorizeAsync(db, pageSession.CookieValue, pageSession.PublicId, pageSession.ProtectionToken, CancellationToken.None));
            Assert.False(await SectorOpeningExchange.AuthorizeAsync(db, pageSession.CookieValue, pageSession.PublicId, new string('b', 64), CancellationToken.None));
            Assert.True(await SectorOpeningExchange.SwitchAsync(db, pageSession.CookieValue, pageSession.PublicId, "ATD-2", CancellationToken.None));
            Assert.False(await SectorOpeningExchange.SwitchAsync(db, pageSession.CookieValue, pageSession.PublicId, "ATD-9", CancellationToken.None));

            var typed = NewCode();
            await InsertCode(db, typed, tenant, patient, encounter, integration, "tipo", "DS23", null, null, true);
            var typedSession = await SectorOpeningExchange.ConsumeAsync(db, typed, CancellationToken.None);
            Assert.NotNull(await SectorOpeningExchange.ReadAsync(db, typedSession!.CookieValue, typedSession.PublicId, CancellationToken.None));
            Assert.False(await SectorOpeningExchange.SwitchAsync(db, typedSession.CookieValue, typedSession.PublicId, "ATD-2", CancellationToken.None));
            await db.ExecuteAsync("update ged.sector_integration set catalog_codes='{DS01}' where id=@integration", new { integration });
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, typedSession.CookieValue, typedSession.PublicId, CancellationToken.None));

            await db.ExecuteAsync("update ged.sector_integration set catalog_codes='{}' where id=@integration", new { integration });
            var documentary = NewCode();
            await InsertCode(db, documentary, tenant, patient, encounter, integration, "documento", "DS23", documentId, versionId, true);
            var documentarySession = await SectorOpeningExchange.ConsumeAsync(db, documentary, CancellationToken.None);
            Assert.NotNull(await SectorOpeningExchange.ReadAsync(db, documentarySession!.CookieValue, documentarySession.PublicId, CancellationToken.None));
            Assert.False(await SectorOpeningExchange.SwitchAsync(db, documentarySession.CookieValue, documentarySession.PublicId, "ATD-2", CancellationToken.None));
            await db.ExecuteAsync("update ged.sector_document_link set version_id=@otherVersion where document_id=@documentId", new { otherVersion = Guid.NewGuid(), documentId });
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, documentarySession.CookieValue, documentarySession.PublicId, CancellationToken.None));

            var pending = NewCode();
            await InsertCode(db, pending, tenant, patient, encounter, integration, "atendimento", null, null, null, false);
            await SectorOpeningExchange.DeactivateAsync(db, tenant, "parceiro", CancellationToken.None);
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, pageSession.CookieValue, pageSession.PublicId, CancellationToken.None));
            Assert.False(await db.ExecuteScalarAsync<bool>("select active from ged.sector_integration where id=@integration", new { integration }));
            Assert.NotNull(await db.ExecuteScalarAsync<DateTime?>("select consumed_at from ged.sector_open_code where code_hash=@hash", new { hash = SectorOpeningExchange.Hash(pending) }));
            Assert.Null(await SectorOpeningExchange.ConsumeAsync(db, pending, CancellationToken.None));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Restricted_catalog_is_enforced_on_every_read_and_opening_races_stay_closed()
    {
        Assert.Contains("todos os tipos ativos", SectorSessionScope.EmptyCatalogMeansAllTypes, StringComparison.Ordinal);
        var name = "ig_abertura_" + Guid.NewGuid().ToString("N");
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
            await db.ExecuteAsync("""
alter table ged.sector_document_link add column document_date date;
alter table ged.sector_document_type add column catalog_order integer not null default 0;
alter table ged.sector_document_type add column synonyms text[] not null default '{}';
create table ged.sector_extracted_field (
    id uuid primary key, tenant_id uuid not null, link_id uuid not null, version_id uuid not null,
    updated_at timestamptz not null default now());
create table ged.sector_finding (
    id uuid primary key, tenant_id uuid not null, link_id uuid not null, updated_at timestamptz not null default now());
create table ged.document_version (
    tenant_id uuid not null, id uuid primary key, content_text text);
""");
            var tenant = Guid.NewGuid();
            var otherTenant = Guid.NewGuid();
            var patient = Guid.NewGuid();
            var encounter = Guid.NewGuid();
            var otherEncounter = Guid.NewGuid();
            var integration = Guid.NewGuid();
            var document23 = Guid.NewGuid();
            var document08 = Guid.NewGuid();
            var version23 = Guid.NewGuid();
            var version08 = Guid.NewGuid();
            var link23 = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var link08 = Guid.Parse("00000000-0000-0000-0000-000000000002");
            var linkSame = Guid.Parse("00000000-0000-0000-0000-000000000003");
            var versionSame = Guid.NewGuid();
            var documentSame = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.sector_patient (id, tenant_id, source_system, source_unit, external_code, display_name)
values (@patient, @tenant, 'MV', '1', '000120', 'Paciente sintético');
insert into ged.sector_encounter (id, tenant_id, patient_id, external_code) values
 (@encounter, @tenant, @patient, 'ATD-1'),
 (@otherEncounter, @tenant, @patient, 'ATD-2');
insert into ged.sector_integration
 (id, tenant_id, client_id, secret_hash, source_system, source_unit, origins, catalog_codes, allow_encounter_list, allow_encounter_switch, active)
values
 (@integration, @tenant, 'parceiro', repeat('b', 64), 'MV', '1', '{https://parceiro.exemplo}', '{DS23}', true, true, true);
insert into ged.sector_document_type (tenant_id, catalog_code, name, catalog_order) values
 (@tenant, 'DS23', 'Prescrição', 23),
 (@tenant, 'DS08', 'Evolução', 8);
insert into ged.document (tenant_id, id) values
 (@tenant, @document23), (@tenant, @document08), (@tenant, @documentSame);
insert into ged.document_version (tenant_id, id, content_text) values
 (@tenant, @version23, 'dipirona 500 mg'),
 (@tenant, @version08, 'dipirona citada na evolução'),
 (@tenant, @versionSame, 'dipirona mesma data');
insert into ged.sector_document_link
 (id, tenant_id, patient_id, encounter_id, document_id, version_id, catalog_code, document_date) values
 (@link23, @tenant, @patient, @encounter, @document23, @version23, 'DS23', date '2026-10-01'),
 (@link08, @tenant, @patient, @encounter, @document08, @version08, 'DS08', date '2026-10-01'),
 (@linkSame, @tenant, @patient, @encounter, @documentSame, @versionSame, 'DS23', date '2026-10-01');
insert into ged.sector_extracted_field (id, tenant_id, link_id, version_id) values
 (gen_random_uuid(), @tenant, @link23, @version23),
 (gen_random_uuid(), @tenant, @link08, @version08);
insert into ged.sector_finding (id, tenant_id, link_id) values
 (gen_random_uuid(), @tenant, @link23),
 (gen_random_uuid(), @tenant, @link08);
""", new { tenant, patient, encounter, otherEncounter, integration, document23, document08, documentSame, version23, version08, versionSame, link23, link08, linkSame });

            SectorSessionAccess Access(string[] catalog, Guid? tenantId = null, Guid? patientId = null, Guid? versionId = null, string mode = "consulta")
                => new(Guid.NewGuid(), tenantId ?? tenant, patientId ?? patient, encounter, null, null, versionId, mode, versionId is null ? "atendimento" : "documento", false, "https://parceiro.exemplo", "corr", DateTimeOffset.UtcNow.AddMinutes(10), catalog.Length == 0 ? ["DS08", "DS23"] : catalog);

            Assert.Equal(new[] { "DS23", "DS23" }, await Codes(db, Access(["DS23"])));
            Assert.Equal(new[] { "DS23" }, await FieldCodes(db, Access(["DS23"])));
            Assert.Equal(new[] { "DS23" }, await FindingCodes(db, Access(["DS23"])));
            Assert.Equal(new[] { "DS23" }, await ChecklistCodes(db, Access(["DS23"])));
            Assert.Equal(new[] { "DS23" }, await Received(db, Access(["DS23"])));
            Assert.True(await Allows(db, Access(["DS23"]), version23));
            Assert.False(await Allows(db, Access(["DS23"]), version08));
            Assert.False(await Allows(db, Access(["DS23"], versionId: version23, mode: "documento"), versionSame));
            Assert.Equal(3, (await Codes(db, Access([]))).Length);
            Assert.Equal(new[] { "DS08" }, await Codes(db, Access(["DS08"])));
            Assert.Empty(await Codes(db, Access(["DS23"], tenantId: otherTenant)));
            Assert.Empty(await Codes(db, Access(["DS23"], patientId: Guid.NewGuid())));

            await db.ExecuteAsync("update ged.document set reg_status='I' where id=@document08", new { document08 });
            Assert.DoesNotContain("DS08", await Codes(db, Access([])));
            Assert.Empty(await FieldCodes(db, Access(["DS08"])));
            Assert.Empty(await FindingCodes(db, Access(["DS08"])));
            await db.ExecuteAsync("update ged.document set reg_status='A' where id=@document08", new { document08 });
            await db.ExecuteAsync("update ged.sector_document_link set reg_status='I' where id=@link08", new { link08 });
            Assert.DoesNotContain("DS08", await Codes(db, Access([])));
            Assert.Equal(new[] { "DS23" }, await Received(db, Access([])));
            await db.ExecuteAsync("update ged.sector_document_link set reg_status='A' where id=@link08", new { link08 });

            var ordered = (await db.QueryAsync<Guid>(PageSql(), PageArgs(Access([]), "%dipirona%", 10, 0))).ToArray();
            Assert.Equal(3, ordered.Length);
            var paged = new List<Guid>();
            for (var index = 0; index < ordered.Length; index++)
                paged.AddRange(await db.QueryAsync<Guid>(PageSql(), PageArgs(Access([]), "%dipirona%", 1, index)));
            Assert.Equal(ordered, paged);
            Assert.Equal(ordered.Length, paged.Distinct().Count());

            var page = NewCode();
            var frame = NewCode();
            await InsertCode(db, page, tenant, patient, encounter, integration, "atendimento", null, null, null, true);
            await InsertCode(db, frame, tenant, patient, encounter, integration, "atendimento", null, null, null, true);
            var pageSession = await SectorOpeningExchange.ConsumeAsync(db, page, CancellationToken.None);
            var frameSession = await SectorOpeningExchange.ConsumeAsync(db, frame, CancellationToken.None);
            var pageAccess = await SectorOpeningExchange.ReadAsync(db, pageSession!.CookieValue, pageSession.PublicId, CancellationToken.None);
            var frameAccess = await SectorOpeningExchange.ReadAsync(db, frameSession!.CookieValue, frameSession.PublicId, CancellationToken.None);
            Assert.Equal(new[] { "DS23" }, pageAccess!.AllowedCatalogCodes);
            Assert.NotNull(frameAccess);
            Assert.False(await SectorOpeningExchange.AuthorizeAsync(db, frameSession.CookieValue, frameSession.PublicId, pageSession.ProtectionToken, CancellationToken.None));
            Assert.False(await SectorOpeningExchange.AuthorizeAsync(db, pageSession.CookieValue, pageSession.PublicId, frameSession.ProtectionToken, CancellationToken.None));
            await db.ExecuteAsync("update ged.sector_integration set catalog_codes='{DS08}' where id=@integration", new { integration });
            var afterChange = await SectorOpeningExchange.ReadAsync(db, pageSession.CookieValue, pageSession.PublicId, CancellationToken.None);
            Assert.Equal(new[] { "DS08" }, afterChange!.AllowedCatalogCodes);
            Assert.Equal(new[] { "DS08" }, await Codes(db, afterChange));
            await db.ExecuteAsync("update ged.sector_integration set catalog_codes='{}' where id=@integration", new { integration });
            var allTypes = await SectorOpeningExchange.ReadAsync(db, pageSession.CookieValue, pageSession.PublicId, CancellationToken.None);
            Assert.Equal(new[] { "DS08", "DS23" }, allTypes!.AllowedCatalogCodes);
            Assert.Equal(3, (await Codes(db, allTypes)).Length);
            var denied = allTypes with { AllowedCatalogCodes = [] };
            Assert.Empty(await Codes(db, denied));
            Assert.Empty(await FieldCodes(db, denied));
            Assert.Empty(await FindingCodes(db, denied));
            Assert.Empty(await ChecklistCodes(db, denied));
            Assert.Empty(await Received(db, denied));
            Assert.False(await Allows(db, denied, version23));
            Assert.False(await Allows(db, denied, version08));

            await db.ExecuteAsync("update ged.sector_open_session set granted_catalog='{DS23}' where public_id=@id", new { id = frameSession.PublicId });
            await db.ExecuteAsync("update ged.sector_integration set catalog_codes='{DS08}' where id=@integration", new { integration });
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, frameSession.CookieValue, frameSession.PublicId, CancellationToken.None));
            Assert.False(await SectorOpeningExchange.SwitchAsync(db, frameSession.CookieValue, frameSession.PublicId, "ATD-2", CancellationToken.None));
            await db.ExecuteAsync("update ged.sector_integration set catalog_codes='{}' where id=@integration", new { integration });
            Assert.True(await SectorOpeningExchange.SwitchAsync(db, pageSession.CookieValue, pageSession.PublicId, "ATD-2", CancellationToken.None));
            await db.ExecuteAsync("update ged.sector_integration set origins='{https://outra.origem}' where id=@integration", new { integration });
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, pageSession.CookieValue, pageSession.PublicId, CancellationToken.None));
            Assert.False(await SectorOpeningExchange.SwitchAsync(db, frameSession.CookieValue, frameSession.PublicId, "ATD-2", CancellationToken.None));
            await db.ExecuteAsync("update ged.sector_integration set origins='{https://parceiro.exemplo}' where id=@integration", new { integration });

            var documentary = NewCode();
            await InsertCode(db, documentary, tenant, patient, encounter, integration, "documento", "DS23", document23, version23, false);
            var documentarySession = await SectorOpeningExchange.ConsumeAsync(db, documentary, CancellationToken.None);
            Assert.NotNull(await SectorOpeningExchange.ReadAsync(db, documentarySession!.CookieValue, documentarySession.PublicId, CancellationToken.None));
            await db.ExecuteAsync("update ged.sector_document_link set version_id=@moved where id=@link23", new { moved = Guid.NewGuid(), link23 });
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, documentarySession.CookieValue, documentarySession.PublicId, CancellationToken.None));

            var raced = NewCode();
            await InsertCode(db, raced, tenant, patient, encounter, integration, "atendimento", null, null, null, false);
            var consumeTask = Task.Run(async () =>
            {
                await using var conn = new NpgsqlConnection(builder.ConnectionString);
                await conn.OpenAsync();
                return await SectorOpeningExchange.ConsumeAsync(conn, raced, CancellationToken.None);
            });
            var deactivateTask = Task.Run(async () =>
            {
                await using var conn = new NpgsqlConnection(builder.ConnectionString);
                await conn.OpenAsync();
                await SectorOpeningExchange.DeactivateAsync(conn, tenant, "parceiro", CancellationToken.None);
            });
            SectorConsumedOpening? racedSession = null;
            try
            {
                await Task.WhenAll(consumeTask, deactivateTask);
                racedSession = consumeTask.Result;
            }
            catch (Exception)
            {
                if (consumeTask.IsCompletedSuccessfully) racedSession = consumeTask.Result;
            }
            Assert.False(await db.ExecuteScalarAsync<bool>("select active from ged.sector_integration where id=@integration", new { integration }));
            Assert.NotNull(await db.ExecuteScalarAsync<DateTime?>("select consumed_at from ged.sector_open_code where code_hash=@hash", new { hash = SectorOpeningExchange.Hash(raced) }));
            if (racedSession is not null)
                Assert.Null(await SectorOpeningExchange.ReadAsync(db, racedSession.CookieValue, racedSession.PublicId, CancellationToken.None));
            Assert.Null(await SectorOpeningExchange.ReadAsync(db, pageSession.CookieValue, pageSession.PublicId, CancellationToken.None));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    private static async Task<string[]> Codes(NpgsqlConnection db, SectorSessionAccess session)
        => (await db.QueryAsync<string>($@"
select l.catalog_code
from ged.sector_document_link l
join ged.sector_document_type t on t.tenant_id=l.tenant_id and t.catalog_code=l.catalog_code and t.active
where true
{SectorSessionScope.LinkFilter}
order by l.id", SectorSessionScope.Args(session))).ToArray();

    private static async Task<string[]> FieldCodes(NpgsqlConnection db, SectorSessionAccess session)
        => (await db.QueryAsync<string>($@"
select l.catalog_code
from ged.sector_extracted_field f
join ged.sector_document_link l on l.id=f.link_id and l.tenant_id=f.tenant_id
where f.version_id=l.version_id
{SectorSessionScope.LinkFilter}
order by l.id", SectorSessionScope.Args(session))).ToArray();

    private static async Task<string[]> FindingCodes(NpgsqlConnection db, SectorSessionAccess session)
        => (await db.QueryAsync<string>($@"
select l.catalog_code
from ged.sector_finding f
join ged.sector_document_link l on l.id=f.link_id and l.tenant_id=f.tenant_id
where f.tenant_id=@tenantId
{SectorSessionScope.LinkFilter}
order by l.id", SectorSessionScope.Args(session))).ToArray();

    private static async Task<string[]> ChecklistCodes(NpgsqlConnection db, SectorSessionAccess session)
        => (await db.QueryAsync<string>("""
select catalog_code
from ged.sector_document_type
where tenant_id=@tenantId and active and (@catalog is null or catalog_code=@catalog)
  and catalog_code = any(@allowedCatalog)
order by catalog_order
""", SectorSessionScope.Args(session))).ToArray();

    private static async Task<string[]> Received(NpgsqlConnection db, SectorSessionAccess session)
        => (await db.QueryAsync<string>($"""
select distinct l.catalog_code
from ged.sector_document_link l
where true
{SectorSessionScope.LinkFilter}
order by l.catalog_code
""", SectorSessionScope.Args(session))).ToArray();

    private static Task<bool> Allows(NpgsqlConnection db, SectorSessionAccess session, Guid asked)
        => db.ExecuteScalarAsync<bool>($@"
select exists(
  select 1 from ged.sector_document_link l
  where l.version_id=@askedVersion
  {SectorSessionScope.LinkFilter})
", new
        {
            tenantId = session.TenantId, patientId = session.PatientId, encounterId = session.EncounterId, expand = false,
            catalog = session.CatalogCode, documentId = session.DocumentId,
            versionId = session.Mode == "documento" ? session.VersionId : null,
            allowedCatalog = session.AllowedCatalogCodes ?? [], askedVersion = asked,
            requireUserAuthorization = session.AuthorizingUserId.HasValue, authorizedDocuments = session.AuthorizedDocumentIds ?? []
        });

    private static string PageSql() => $"""
select l.id
from ged.sector_document_link l
join ged.sector_document_type t on t.tenant_id=l.tenant_id and t.catalog_code=l.catalog_code and t.active
join ged.document_version v on v.tenant_id=l.tenant_id and v.id=l.version_id
where true
{SectorSessionScope.LinkFilter}
  and coalesce(v.content_text, '') ilike @pattern
order by l.document_date nulls last, t.catalog_order, l.id
limit @take offset @skip
""";

    private static object PageArgs(SectorSessionAccess session, string pattern, int take, int skip)
        => new
        {
            tenantId = session.TenantId, patientId = session.PatientId, encounterId = session.EncounterId, expand = false,
            catalog = session.CatalogCode, documentId = session.DocumentId, versionId = (Guid?)null,
            allowedCatalog = session.AllowedCatalogCodes ?? [], pattern, take, skip,
            requireUserAuthorization = session.AuthorizingUserId.HasValue, authorizedDocuments = session.AuthorizedDocumentIds ?? []
        };

    private static async Task InsertCode(NpgsqlConnection db, string code, Guid tenant, Guid patient, Guid encounter, Guid integration, string scope, string? catalog, Guid? documentId, Guid? versionId, bool allowSwitch)
    {
        await db.ExecuteAsync("""
insert into ged.sector_open_code
 (id, tenant_id, code_hash, patient_id, encounter_id, catalog_code, document_id, version_id, mode, scope_kind, allow_encounter_switch, origin, integration_id, correlation_id, expires_at)
values
 (gen_random_uuid(), @tenant, @hash, @patient, @encounter, @catalog, @documentId, @versionId, @mode, @scope, @allowSwitch, 'https://parceiro.exemplo', @integration, 'corr', now() + interval '15 minutes')
""", new
        {
            tenant, hash = SectorOpeningExchange.Hash(code), patient, encounter, catalog, documentId, versionId,
            mode = scope == "documento" ? "documento" : "consulta", scope, allowSwitch, integration
        });
    }

    private static string NewCode() => Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());

    private const string Schema = """
create schema if not exists ged;
create table ged.sector_patient (
    id uuid primary key, tenant_id uuid not null, source_system text not null, source_unit text not null default '',
    external_code text not null, display_name text not null, identifier_kind text not null default 'ID');
create table ged.sector_encounter (
    id uuid primary key, tenant_id uuid not null, patient_id uuid not null, external_code text not null);
create table ged.sector_integration (
    id uuid primary key, tenant_id uuid not null, client_id text not null, secret_hash char(64) not null,
    source_system text not null, source_unit text not null default '', origins text[] not null default '{}',
    catalog_codes text[] not null default '{}', allow_encounter_list boolean not null default false,
    allow_encounter_switch boolean not null default false, max_minutes integer not null default 15, active boolean not null default true);
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
    expires_at timestamptz not null, revoked_at timestamptz, protection_hash char(64), granted_catalog text[], authorizing_user_id uuid,
    constraint scope_ok check (scope_kind in ('documento','tipo','atendimento')));
create table ged.sector_document_type (
    tenant_id uuid not null, catalog_code text not null, name text not null, active boolean not null default true,
    primary key (tenant_id, catalog_code));
create table ged.document (tenant_id uuid not null, id uuid not null, reg_status char(1) not null default 'A', primary key (tenant_id, id));
create table ged.sector_document_link (
    id uuid primary key, tenant_id uuid not null, patient_id uuid not null, encounter_id uuid not null,
    document_id uuid not null, version_id uuid not null, catalog_code text not null, reg_status char(1) not null default 'A');
""";
}
