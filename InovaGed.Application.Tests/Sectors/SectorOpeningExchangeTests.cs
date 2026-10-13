using Dapper;
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
 (id, tenant_id, client_id, secret_hash, source_system, source_unit, catalog_codes, allow_encounter_list, allow_encounter_switch, active)
values
 (@integration, @tenant, 'parceiro', repeat('a', 64), 'MV', '1', '{DS23}', true, false, true);
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
    origin text not null, integration_id uuid not null, correlation_id text not null, expires_at timestamptz not null, consumed_at timestamptz);
create table ged.sector_open_session (
    id uuid primary key, tenant_id uuid not null, integration_id uuid not null, cookie_hash char(64) not null unique,
    public_id uuid not null unique, patient_id uuid not null, encounter_id uuid not null, catalog_code text,
    document_id uuid, version_id uuid, mode text not null, scope_kind text not null,
    allow_encounter_switch boolean not null default false, origin text not null, correlation_id text not null,
    expires_at timestamptz not null, revoked_at timestamptz, protection_hash char(64),
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
