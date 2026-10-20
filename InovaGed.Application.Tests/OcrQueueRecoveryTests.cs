using System.Data;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Infrastructure.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Application.Tests;

public sealed class OcrPgFactAttribute : FactAttribute
{
    public OcrPgFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("INOVAGED_OCR_TEST_DSN")))
            Skip = "Defina INOVAGED_OCR_TEST_DSN para um banco descartável inovaged_ocr_tests.";
    }
}

public sealed class OcrQueueRecoveryTests : IAsyncLifetime
{
    private NpgsqlConnection? _db;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _version = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var dsn = System.Environment.GetEnvironmentVariable("INOVAGED_OCR_TEST_DSN");
        if (string.IsNullOrWhiteSpace(dsn)) return;
        if (new NpgsqlConnectionStringBuilder(dsn).Database != "inovaged_ocr_tests")
            throw new InvalidOperationException("Os testes exigem o banco descartável inovaged_ocr_tests.");
        _db = new NpgsqlConnection(dsn);
        await _db.OpenAsync();
        await _db!.ExecuteAsync("""
            CREATE SCHEMA IF NOT EXISTS ged;
            DO $$ BEGIN CREATE TYPE ged.ocr_status_enum AS ENUM ('PENDING','PROCESSING','COMPLETED','ERROR','CANCELLED');
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            CREATE TABLE IF NOT EXISTS ged.ocr_job (
                id bigserial PRIMARY KEY, tenant_id uuid NOT NULL, document_version_id uuid NOT NULL,
                requested_by uuid, invalidate_digital_signatures boolean NOT NULL DEFAULT false,
                status ged.ocr_status_enum NOT NULL, requested_at timestamptz NOT NULL DEFAULT now(),
                started_at timestamptz, finished_at timestamptz, lease_expires_at timestamptz,
                attempts integer NOT NULL DEFAULT 0, last_attempt_at timestamptz, next_attempt_at timestamptz,
                error_message text, error_details_json jsonb, failure_code text,
                locked_by text, locked_at timestamptz, updated_at timestamptz,
                cancel_requested boolean NOT NULL DEFAULT false, result_document_id uuid, result_version_id uuid);
            CREATE TABLE IF NOT EXISTS ged.document_search (tenant_id uuid, version_id uuid, ocr_text text);
            TRUNCATE ged.ocr_job;
            """);
    }

    public async Task DisposeAsync() { if (_db is not null) await _db.DisposeAsync(); }
    private OcrJobRepository Repository(OcrOptions? options = null) => new(new Factory(),
        NullLogger<OcrJobRepository>.Instance, Options.Create(options ?? new OcrOptions()));
    private Task<long> Enqueue(OcrJobRepository repo, bool force = false) =>
        repo.EnqueueAsync(_tenant, _version, _actor, force, default);

    [OcrPgFact]
    public async Task Recovery_migration_is_idempotent_and_preserves_existing_jobs()
    {
        var id = await Enqueue(Repository());
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "InovaGed.sln"))) root = root.Parent;
        var sql = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database", "migrations", "2026_10_20_ocr_lease_recovery.sql"));
        await _db!.ExecuteAsync(sql);
        await _db!.ExecuteAsync(sql);
        Assert.Equal("PENDING", await Status(id));
    }

    [OcrPgFact]
    public async Task Sensitive_egress_migration_defaults_to_blocked_and_requires_a_formal_reference()
    {
        await _db!.ExecuteAsync("CREATE TABLE IF NOT EXISTS ged.ai_tenant_policy (tenant_id uuid PRIMARY KEY, enabled boolean NOT NULL DEFAULT false);");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "InovaGed.sln"))) root = root.Parent;
        var sql = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database", "migrations", "2026_10_20_ai_sensitive_egress.sql"));
        await _db!.ExecuteAsync(sql);
        await _db!.ExecuteAsync(sql);
        await _db!.ExecuteAsync("insert into ged.ai_tenant_policy(tenant_id,enabled) values(@tenant,true)", new { tenant = _tenant });
        Assert.False(await _db.ExecuteScalarAsync<bool>("select allow_sensitive_external from ged.ai_tenant_policy where tenant_id=@tenant", new { tenant = _tenant }));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => _db.ExecuteAsync("update ged.ai_tenant_policy set allow_sensitive_external=true where tenant_id=@tenant", new { tenant = _tenant }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        await _db!.ExecuteAsync("update ged.ai_tenant_policy set allow_sensitive_external=true,external_authorization_reference='AUT-SYNTHETIC-001' where tenant_id=@tenant", new { tenant = _tenant });
    }

    [OcrPgFact]
    public async Task Legacy_completed_job_without_text_can_be_reprocessed_without_invalidating_signatures()
    {
        var repo = Repository();
        var previous = await Enqueue(repo);
        await repo.DequeueAndMarkProcessingAsync(default);
        await repo.MarkCompletedAsync(previous, default);
        Assert.False(await repo.HasCompletedAsync(_tenant, _version, default));
        var next = await Enqueue(Repository());
        Assert.NotEqual(previous, next);
    }

    [OcrPgFact]
    public async Task Completed_job_with_persisted_text_is_not_processed_twice()
    {
        var repo = Repository();
        var id = await Enqueue(repo);
        await repo.DequeueAndMarkProcessingAsync(default);
        var derivedVersion = Guid.NewGuid();
        await repo.MarkCompletedAsync(id, Guid.NewGuid(), derivedVersion, default);
        await _db!.ExecuteAsync("insert into ged.document_search(tenant_id,version_id,ocr_text) values(@tenant,@version,'texto sintético')",
            new { tenant = _tenant, version = derivedVersion });
        Assert.True(await repo.HasCompletedAsync(_tenant, _version, default));
        Assert.Equal(id, await Enqueue(Repository()));
    }

    [OcrPgFact]
    public async Task Cancellation_flag_from_an_interrupted_caller_is_finalized()
    {
        var id = await Enqueue(Repository());
        await _db!.ExecuteAsync("update ged.ocr_job set status='PROCESSING',cancel_requested=true where id=@id", new { id });
        Assert.Null(await Repository().DequeueAndMarkProcessingAsync(default));
        Assert.Equal("CANCELLED", await Status(id));
    }

    [OcrPgFact]
    public async Task Concurrent_enqueue_deduplicates_even_for_forced_requests()
    {
        var ids = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Enqueue(Repository(), true)));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await _db!.ExecuteScalarAsync<int>("select count(*) from ged.ocr_job"));
    }

    [OcrPgFact]
    public async Task Concurrent_consumers_respect_global_capacity()
    {
        await Enqueue(Repository());
        await Repository().EnqueueAsync(_tenant, Guid.NewGuid(), _actor, false, default);
        var jobs = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Repository().DequeueAndMarkProcessingAsync(default)));
        Assert.Single(jobs, j => j is not null);
        Assert.Equal(1, await _db!.ExecuteScalarAsync<int>("select count(*) from ged.ocr_job where status='PROCESSING'"));
    }

    [OcrPgFact]
    public async Task Expired_lease_is_reclaimed_and_stale_worker_cannot_change_state()
    {
        var old = Repository();
        var id = await Enqueue(old);
        await old.DequeueAndMarkProcessingAsync(default);
        await _db!.ExecuteAsync("update ged.ocr_job set lease_expires_at=now()-interval '1 minute' where id=@id", new { id });
        var current = Repository();
        Assert.Equal(id, (await current.DequeueAndMarkProcessingAsync(default))!.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => old.RenewLeaseAsync(id, default));
        await old.MarkCompletedAsync(id, default);
        await old.MarkErrorAsync(id, "stale", default);
        await old.MarkRetryAsync(id, 1, DateTimeOffset.UtcNow, "stale", default);
        Assert.Equal("PROCESSING", await Status(id));
        Assert.Equal(2, await current.GetAttemptsAsync(id, default));
        await current.MarkCompletedAsync(id, default);
        Assert.Equal("COMPLETED", await Status(id));
    }

    [OcrPgFact]
    public async Task Null_lease_from_legacy_crash_is_recoverable()
    {
        var repo = Repository();
        var id = await Enqueue(repo);
        await _db!.ExecuteAsync("update ged.ocr_job set status='PROCESSING', lease_expires_at=null where id=@id", new { id });
        Assert.Equal(id, (await repo.DequeueAndMarkProcessingAsync(default))!.Id);
        await repo.RenewLeaseAsync(id, default);
    }

    [OcrPgFact]
    public async Task Repeated_crashes_end_in_diagnosed_error_queue()
    {
        var id = await Enqueue(Repository());
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.Equal(id, (await Repository().DequeueAndMarkProcessingAsync(default))!.Id);
            await _db!.ExecuteAsync("update ged.ocr_job set lease_expires_at=now()-interval '1 minute' where id=@id", new { id });
        }
        Assert.Null(await Repository().DequeueAndMarkProcessingAsync(default));
        Assert.Equal("ERROR", await Status(id));
        Assert.Equal("OCR_ATTEMPTS_EXHAUSTED", await _db!.ExecuteScalarAsync<string>("select failure_code from ged.ocr_job where id=@id", new { id }));
    }

    [OcrPgFact]
    public async Task Retry_survives_restart_and_respects_scheduled_backoff()
    {
        var repo = Repository();
        var id = await Enqueue(repo);
        await repo.DequeueAndMarkProcessingAsync(default);
        await repo.MarkRetryAsync(id, 1, DateTimeOffset.UtcNow.AddMinutes(5), "temporário", default);
        Assert.Null(await Repository().DequeueAndMarkProcessingAsync(default));
        await _db!.ExecuteAsync("update ged.ocr_job set next_attempt_at=now()-interval '1 second' where id=@id", new { id });
        Assert.Equal(id, (await Repository().DequeueAndMarkProcessingAsync(default))!.Id);
        Assert.Equal(2, await repo.GetAttemptsAsync(id, default));
    }

    [OcrPgFact]
    public async Task Cancellation_cannot_be_overwritten_by_completion_or_failure()
    {
        var repo = Repository();
        var id = await Enqueue(repo);
        await repo.DequeueAndMarkProcessingAsync(default);
        await _db!.ExecuteAsync("update ged.ocr_job set status='CANCELLED',cancel_requested=true where id=@id", new { id });
        await repo.MarkCompletedAsync(id, default);
        await repo.MarkErrorAsync(id, "late", default);
        await repo.MarkRetryAsync(id, 1, DateTimeOffset.UtcNow, "late", default);
        Assert.Equal("CANCELLED", await Status(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RenewLeaseAsync(id, default));
    }

    [OcrPgFact]
    public async Task Heartbeat_cannot_keep_job_alive_past_operational_limit()
    {
        var repo = Repository(new OcrOptions { MaxJobMinutes = 1 });
        var id = await Enqueue(repo);
        await repo.DequeueAndMarkProcessingAsync(default);
        await _db!.ExecuteAsync("update ged.ocr_job set started_at=now()-interval '2 minutes' where id=@id", new { id });
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RenewLeaseAsync(id, default));
    }

    [OcrPgFact]
    public async Task Identical_version_identifier_in_another_tenant_is_not_deduplicated()
    {
        var first = await Enqueue(Repository());
        var second = await Repository().EnqueueAsync(Guid.NewGuid(), _version, _actor, false, default);
        Assert.NotEqual(first, second);
    }

    private Task<string?> Status(long id) => _db!.ExecuteScalarAsync<string>("select status::text from ged.ocr_job where id=@id", new { id });
    private sealed class Factory : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => new NpgsqlConnection(System.Environment.GetEnvironmentVariable("INOVAGED_OCR_TEST_DSN"));
        public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = (NpgsqlConnection)CreateConnection();
            await conn.OpenAsync(ct);
            return conn;
        }
    }
}
