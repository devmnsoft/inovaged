using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using Npgsql;

namespace InovaGed.Application.Tests;

[CollectionDefinition("Document AI PostgreSQL", DisableParallelization = true)]
public sealed class DocumentAiPostgresCollection { }

[Collection("Document AI PostgreSQL")]
public sealed class DocumentAiUpgradePostgresTests
{
    [PgGatedFact]
    public async Task Legacy_upgrade_preserves_every_revision_and_audit_link_including_partial_reexecution()
    {
        foreach (var scenario in new[] { "empty", "single", "equivalent", "different", "partial" })
        {
            await using var connection = new NpgsqlConnection(PgGate.Dsn());
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            var schema = "upgrade_" + Guid.NewGuid().ToString("N");
            await connection.ExecuteAsync($"create schema {schema}; create table {schema}.ai_execution(id uuid primary key);", transaction: tx);
            async Task Apply(string name) => await connection.ExecuteAsync((await File.ReadAllTextAsync(Path.Combine(Root(), "database", "migrations", name))).Replace("ged.", schema + "."), transaction: tx);
            await Apply("2026_10_05_document_ai_application_integrity.sql");
            await connection.ExecuteAsync($"create table {schema}.audit_link(id uuid primary key, application_id uuid references {schema}.ai_suggestion_application(id));", transaction: tx);
            var tenant = Guid.NewGuid(); var execution = Guid.NewGuid(); var document = Guid.NewGuid();
            var version = Guid.NewGuid(); var reviewer = Guid.NewGuid();
            const string task = "SuggestMetadata";
            var canonical = ReviewIdentity.OperationKey(tenant, execution, document, version, reviewer, task);
            await connection.ExecuteAsync($"insert into {schema}.ai_execution(id) values(@execution)", new { execution }, tx);
            var count = scenario == "empty" ? 0 : scenario == "single" ? 1 : 3;
            var ids = new List<Guid>();
            for (var i = 0; i < count; i++)
            {
                var id = Guid.NewGuid(); ids.Add(id);
                var decision = ReviewIdentity.MetadataJson(true, scenario == "different" ? "Title " + i : "Title", false, null, false, null, null);
                await connection.ExecuteAsync($"""
insert into {schema}.ai_suggestion_application(id,tenant_id,execution_id,document_id,version_id,task,reviewer_id,decision_fingerprint,decision_json,outcome,created_at)
values(@id,@tenant,@execution,@document,@version,@task,@reviewer,@fingerprint,cast(@decision as jsonb),'Applied',@at);
insert into {schema}.audit_link values(@id,@id);
""", new { id, tenant, execution, document, version, task, reviewer, fingerprint = ReviewIdentity.Fingerprint(decision + new string(' ', i)), decision, at = DateTime.UtcNow.AddMinutes(i) }, tx);
            }
            if (scenario == "equivalent")
            {
                await tx.SaveAsync("broken");
                var failure = await Assert.ThrowsAsync<PostgresException>(() => Apply("2026_10_06_document_ai_review_recovery.sql"));
                Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, failure.SqlState);
                await tx.RollbackAsync("broken");
            }
            if (scenario == "partial")
                await connection.ExecuteAsync($"alter table {schema}.ai_suggestion_application add column operation_key varchar(64); update {schema}.ai_suggestion_application set operation_key=@canonical where id=@id", new { canonical, id = ids[0] }, tx);
            var before = await connection.QueryAsync<string>($"select (to_jsonb(a)-'operation_key')::text from {schema}.ai_suggestion_application a order by id", transaction: tx);
            await Apply("2026_10_06_document_ai_review_identity_preflight.sql");
            await Apply("2026_10_06_document_ai_review_recovery.sql");
            var keys = (await connection.QueryAsync<string>($"select operation_key from {schema}.ai_suggestion_application order by id", transaction: tx)).ToArray();
            Assert.All(keys, key => Assert.Equal(64, key.Length));
            Assert.Equal(count, keys.Distinct().Count());
            if (count > 0)
                Assert.Equal(ids[0], await connection.ExecuteScalarAsync<Guid>($"select id from {schema}.ai_suggestion_application where operation_key=@canonical", new { canonical }, tx));
            await Apply("2026_10_06_document_ai_review_identity_preflight.sql");
            await Apply("2026_10_06_document_ai_review_recovery.sql");
            Assert.Equal(keys, (await connection.QueryAsync<string>($"select operation_key from {schema}.ai_suggestion_application order by id", transaction: tx)).ToArray());
            Assert.Equal(before, await connection.QueryAsync<string>($"select (to_jsonb(a)-'operation_key')::text from {schema}.ai_suggestion_application a order by id", transaction: tx));
            Assert.Equal(count, await connection.ExecuteScalarAsync<int>($"select count(*) from {schema}.audit_link l join {schema}.ai_suggestion_application a on a.id=l.application_id", transaction: tx));
            await tx.RollbackAsync();
        }
    }

    [PgGatedFact]
    public async Task Signature_bridge_preserves_legacy_checks_and_rejects_foreign_run_identity()
    {
        await using var connection = new NpgsqlConnection(PgGate.Dsn());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var schema = "signature_upgrade_" + Guid.NewGuid().ToString("N");
        var tenant = Guid.NewGuid(); var signature = Guid.NewGuid(); var run = Guid.NewGuid();
        await connection.ExecuteAsync($"""
create schema {schema};
create table {schema}.document_signature(id uuid primary key, signed_at timestamptz);
create table {schema}.signature_validation_run(id uuid primary key,tenant_id uuid,signature_id uuid);
create table {schema}.signature_validation_check(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,signature_id uuid not null,check_name text not null,check_status text not null);
insert into {schema}.document_signature values(@signature,'2024-02-03T10:00:00Z');
insert into {schema}.signature_validation_run values(@run,@tenant,@signature);
insert into {schema}.signature_validation_check(tenant_id,signature_id,check_name,check_status) values(@tenant,@signature,'Legacy evidence','INCONCLUSIVE');
""", new { tenant, signature, run }, tx);
        var sql = (await File.ReadAllTextAsync(Path.Combine(Root(), "database/migrations/2026_07_signature_runtime_prerequisites.sql")))
            .Replace("ged.", schema + ".").Replace("'ged'", "'" + schema + "'");
        await connection.ExecuteAsync(sql, transaction: tx);
        await connection.ExecuteAsync(sql, transaction: tx);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>($"select count(*) from {schema}.document_signature where created_at=signed_at", transaction: tx));
        Assert.Equal("INCONCLUSIVE", await connection.ExecuteScalarAsync<string>($"select status from {schema}.signature_validation_check where name='Legacy evidence'", transaction: tx));
        var linked = await connection.ExecuteScalarAsync<Guid>($"insert into {schema}.signature_validation_check(tenant_id,validation_run_id,name,status) values(@tenant,@run,'New evidence','INCONCLUSIVE') returning signature_id", new { tenant, run }, tx);
        Assert.Equal(signature, linked);
        await tx.SaveAsync("foreign_run");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync($"insert into {schema}.signature_certificate_chain(tenant_id,validation_run_id,chain_order,certificate_der) values(@foreign,@run,0,decode('00','hex'))", new { foreign = Guid.NewGuid(), run }, tx));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
        await tx.RollbackAsync("foreign_run");
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>($"select count(*) from {schema}.signature_certificate_chain", transaction: tx));
        await tx.RollbackAsync();
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
