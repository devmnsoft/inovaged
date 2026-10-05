using System.Net;
using System.Text.Json;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Infrastructure.ArtificialIntelligence;
using InovaGed.Infrastructure.Common.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Application.Tests;

/// <summary>
/// Discovery-time gate for scratch-PostgreSQL tests. The pinned xunit v2 stack (xunit 2.9.2 +
/// xunit.runner.visualstudio 2.8.2) does not honor a SkipException thrown at runtime, so the
/// availability probe runs once and is converted into FactAttribute.SkipReason, which every
/// v2 runner reads during discovery.
/// </summary>
internal static class PgGate
{
    public static string? UnavailableReason => Probe.Value;
    public static string Dsn() => System.Environment.GetEnvironmentVariable("INOVAGED_AI_PG_DSN")!;

    private static readonly Lazy<string?> Probe = new(() =>
    {
        var dsn = System.Environment.GetEnvironmentVariable("INOVAGED_AI_PG_DSN");
        if (string.IsNullOrWhiteSpace(dsn))
            return "Defina INOVAGED_AI_PG_DSN apontando para um PostgreSQL descartavel com as migrations de governanca de IA aplicadas.";
        try
        {
            using var conn = new NpgsqlConnection(dsn); conn.Open();
            using var cmd = new NpgsqlCommand("select to_regclass('ged.ai_execution') is not null", conn);
            return Convert.ToBoolean(cmd.ExecuteScalar()) ? null : "O banco definido em INOVAGED_AI_PG_DSN nao possui ged.ai_execution.";
        }
        catch (Exception ex) { return $"Banco de teste inacessivel ({ex.GetType().Name})."; }
    });
}

/// <summary>FactAttribute that is skipped at discovery when the scratch database is unavailable.</summary>
public sealed class PgGatedFactAttribute : FactAttribute
{
    public PgGatedFactAttribute()
    {
        var reason = PgGate.UnavailableReason;
        if (reason is not null) Skip = reason;
    }
}

/// <summary>
/// Behavioral tests against a real PostgreSQL instance (scratch database with the AI governance
/// migrations applied). Gated by INOVAGED_AI_PG_DSN; skipped when the variable is absent so CI
/// without PostgreSQL keeps working. The database is treated as disposable: each test truncates
/// the governance tables first.
/// </summary>
public sealed class PostgresAiGovernanceStoreBehaviorTests : IAsyncLifetime
{
    private NpgsqlConnection? _conn;

    public Task InitializeAsync()
    {
        if (PgGate.UnavailableReason is not null) return Task.CompletedTask;
        _conn = new NpgsqlConnection(PgGate.Dsn()); _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
do $cleanup$
declare tables text := 'ged.ai_execution, ged.ai_monthly_usage, ged.ai_tenant_policy';
begin
  if to_regclass('ged.ai_retention_recalc_pending') is not null then
    tables := 'ged.ai_retention_recalc_pending, ' || tables;
  end if;
  if to_regclass('ged.ai_suggestion_application') is not null then
    tables := 'ged.ai_suggestion_application, ' || tables;
  end if;
  execute 'truncate ' || tables;
end
$cleanup$;
""";
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() { if (_conn is not null) await _conn.DisposeAsync(); }

    [PgGatedFact]
    public async Task Reserve_holds_estimated_quota_and_blocks_excess()
    {
        var tenant = await SeedTenant(limit: 1000); var user = Guid.NewGuid(); var store = Store;
        await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 400, CancellationToken.None);
        Assert.Equal(400L, (await Usage(tenant)).Reserved);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 700, CancellationToken.None));
        Assert.Equal(400L, (await Usage(tenant)).Reserved); // failed reserve rolled back, no leftover execution
        Assert.Equal(1, await One<int>("select count(*) from ged.ai_execution where tenant_id=@t", new { t = tenant }));

        await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 600, CancellationToken.None);
        Assert.Equal(1000L, (await Usage(tenant)).Reserved); // exactly at the limit is still allowed
    }

    [PgGatedFact]
    public async Task Concurrent_reserves_claim_the_quota_only_once()
    {
        var tenant = await SeedTenant(limit: 1000); var user = Guid.NewGuid();
        var one = Run(Store); var two = Run(Store); // each ReserveAsync opens its own connection
        var results = await Task.WhenAll(one, two);
        int ok = results.Count(r => r.Exception == null), failed = results.Count(r => r.Exception is InvalidOperationException);
        Assert.Equal(1, ok); Assert.Equal(1, failed);
        Assert.Equal(600L, (await Usage(tenant)).Reserved);
        Assert.Equal(1, await One<int>("select count(*) from ged.ai_execution where tenant_id=@t", new { t = tenant }));

        Task<ReservationOutcome> Run(PostgresAiGovernanceStore store) => Task.Run(async () =>
        {
            try { return new ReservationOutcome(await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 600, CancellationToken.None), null); }
            catch (Exception ex) { return new ReservationOutcome(null, ex); }
        });
    }

    [PgGatedFact]
    public async Task Idempotency_key_reuses_identical_request_and_conflicts_on_different_content()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store; var key = Key();
        var first = await store.ReserveAsync(Request(tenant, user, key), "Groq", "test-model", 1, 100, CancellationToken.None);
        var again = await store.ReserveAsync(Request(tenant, user, key), "Groq", "test-model", 1, 100, CancellationToken.None);
        Assert.False(again.IsOwner); Assert.Equal(first.ExecutionId, again.ExecutionId);
        Assert.Equal(AiExecutionState.Reserved, again.State);
        Assert.Equal(1, await One<int>("select count(*) from ged.ai_execution where id=@id", new { id = first.ExecutionId }));

        await Assert.ThrowsAsync<AiIdempotencyConflictException>(() => store.ReserveAsync(Request(tenant, user, key, instructions: "conteudo diferente"), "Groq", "test-model", 1, 100, CancellationToken.None));
    }

    [PgGatedFact]
    public async Task MarkRunning_requires_current_policy_revision_before_any_send()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 100, CancellationToken.None);
        await Run("update ged.ai_tenant_policy set revision=2,updated_at=now() where tenant_id=@t", new { t = tenant });

        Assert.False(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        // The governed boundary settles that blocked lease: never sent, never reached, zero consumption.
        await store.CompleteAsync(lease.ExecutionId, new AiResult(false, null, null, null, "Groq", "test-model", AiFailureKind.Disabled, "politica alterada"), 100, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(0L, (await Usage(tenant)).Consumed);
    }

    [PgGatedFact]
    public async Task MarkSent_stamps_the_real_send_instant_and_not_after_settlement()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 100, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));

        Assert.Null(await Scalar<DateTimeOffset?>(null, "select sent_at from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.True(await store.MarkSentAsync(lease.ExecutionId, CancellationToken.None));
        var stamped = await Scalar<DateTime>(null, "select sent_at::timestamp from ged.ai_execution where id=@id", new { id = lease.ExecutionId });
        Assert.True(await store.MarkSentAsync(lease.ExecutionId, CancellationToken.None)); // idempotent while running/unsettled
        Assert.Equal(stamped, await Scalar<DateTime>(null, "select sent_at::timestamp from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));

        await store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 50), 100, TimeSpan.FromMilliseconds(10), CancellationToken.None);
        Assert.False(await store.MarkSentAsync(lease.ExecutionId, CancellationToken.None)); // settled: send instant is fixed
    }

    [PgGatedFact]
    public async Task Successful_completion_settles_exactly_once_and_a_late_duplicate_never_charges_twice()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 500, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        Assert.True(await store.MarkSentAsync(lease.ExecutionId, CancellationToken.None));

        await store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 120, text: "resumo final"), 500, TimeSpan.FromMilliseconds(1234), CancellationToken.None);
        Assert.Equal(new Monthly(0, 120), await Usage(tenant));
        var row = await One<ExecutionSnapshot>("select state as \"State\", settled_tokens as \"SettledTokens\", usage_estimated as \"UsageEstimated\", duration_ms as \"DurationMs\", result_json is not null as \"HasResultJson\" from ged.ai_execution where id=@id", new { id = lease.ExecutionId });
        Assert.Equal("Completed", row.State); Assert.Equal(120L, row.SettledTokens);
        Assert.False(row.UsageEstimated); Assert.True(row.HasResultJson);
        Assert.Equal(1234L, row.DurationMs);
        // timestamptz comes back as DateTime through Npgsql; re-anchor deterministically in UTC instead of casting.
        var expiresUtc = await One<DateTime>("select (result_expires_at at time zone 'UTC')::timestamp from ged.ai_execution where id=@id", new { id = lease.ExecutionId });
        var resultExpiresAt = new DateTimeOffset(expiresUtc, TimeSpan.Zero);
        Assert.InRange(resultExpiresAt, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));

        // Worker retries the same completion after the first one already settled: nothing moves.
        await store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 120, text: "resumo final"), 500, TimeSpan.FromMilliseconds(1234), CancellationToken.None);
        Assert.Equal(new Monthly(0, 120), await Usage(tenant));
        Assert.Equal(120L, await One<long>("select settled_tokens from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(expiresUtc, await One<DateTime>("select (result_expires_at at time zone 'UTC')::timestamp from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Null(await Scalar<DateTime?>("select usage_reconciled_at::timestamp from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
    }

    [PgGatedFact]
    public async Task Late_response_after_expiration_reconciles_without_charging_twice()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 500, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        Assert.True(await store.MarkSentAsync(lease.ExecutionId, CancellationToken.None));
        await BackdateExpiration(lease.ExecutionId);
        Assert.Equal(1, await store.ExpireReservationsAsync(CancellationToken.None));

        // Sent before expiring: the reservation settles as estimated usage, outcome unknown.
        Assert.Equal("RemoteOutcomeUnknown", await One<string>("select state from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(new Monthly(0, 500), await Usage(tenant));
        Assert.True(await Scalar<bool>("select usage_estimated from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));

        // Real usage arrives after the estimate was settled: adjust the original period once.
        await store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 120, text: "resumo tardio"), 500, TimeSpan.FromMilliseconds(30_000), CancellationToken.None);
        Assert.Equal("Completed", await One<string>("select state from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(new Monthly(0, 120), await Usage(tenant));
        Assert.Equal(500L, await One<long>("select settled_tokens from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(120L, await One<long>("select reported_total_tokens from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(-380L, await One<long>("select reconciled_delta from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.NotNull(await Scalar<DateTime?>("select usage_reconciled_at::timestamp from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));

        await store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 80, text: "segunda resposta"), 500, TimeSpan.FromMilliseconds(30_000), CancellationToken.None);
        Assert.Equal(new Monthly(0, 120), await Usage(tenant));
        Assert.Equal(-380L, await One<long>("select reconciled_delta from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal("resumo tardio", (await store.GetExecutionAsync(tenant, user, lease.ExecutionId, CancellationToken.None))!.Result!.Text);
    }

    [PgGatedFact]
    public async Task Never_sent_reservation_expires_with_zero_consumption_and_a_late_result_never_charges()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 500, CancellationToken.None);
        await BackdateExpiration(lease.ExecutionId);
        Assert.Equal(1, await store.ExpireReservationsAsync(CancellationToken.None));

        Assert.Equal("Expired", await One<string>("select state from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(new Monthly(0, 0), await Usage(tenant));
        Assert.Equal(0L, await One<long>("select settled_tokens from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));

        await store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 120, text: "chegou depois"), 500, TimeSpan.FromMilliseconds(30_000), CancellationToken.None);
        Assert.Equal("Completed", await One<string>("select state from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(new Monthly(0, 0), await Usage(tenant)); // it never left this host: zero forever
        Assert.Null(await Scalar<long?>("select reconciled_delta from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
    }

    [PgGatedFact]
    public async Task Completed_result_survives_a_later_failure_and_does_not_extend_retention()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 200, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        Assert.True(await store.MarkSentAsync(lease.ExecutionId, CancellationToken.None));
        await store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 40, text: "conclusao valida"), 200, TimeSpan.FromMilliseconds(10), CancellationToken.None);
        var expires = await One<DateTime>("select (result_expires_at at time zone 'UTC')::timestamp from ged.ai_execution where id=@id", new { id = lease.ExecutionId });

        await store.CompleteAsync(lease.ExecutionId, new AiResult(false, "falha antiga", null, new AiUsage(1, 1, 2), "Groq", "test-model", AiFailureKind.Internal, "repeticao antiga", ProviderReached: true), 200, TimeSpan.FromMilliseconds(99), CancellationToken.None);
        Assert.Equal("Completed", await One<string>("select state from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal("None", await One<string>("select failure_kind from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(expires, await One<DateTime>("select (result_expires_at at time zone 'UTC')::timestamp from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(new Monthly(0, 40), await Usage(tenant));
        Assert.Contains("conclusao valida", await One<string>("select result_json::text from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
    }

    [PgGatedFact]
    public async Task Concurrent_late_usage_adjusts_the_estimate_only_once()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 500, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        Assert.True(await store.MarkSentAsync(lease.ExecutionId, CancellationToken.None));
        await BackdateExpiration(lease.ExecutionId);
        Assert.Equal(1, await store.ExpireReservationsAsync(CancellationToken.None));

        await Task.WhenAll(
            store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 120, text: "a"), 500, TimeSpan.FromMilliseconds(1), CancellationToken.None),
            store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 80, text: "b"), 500, TimeSpan.FromMilliseconds(1), CancellationToken.None));

        var consumed = (await Usage(tenant)).Consumed;
        Assert.Contains(consumed, new long[] { 120, 80 });
        Assert.Equal(consumed - 500, await One<long>("select reconciled_delta from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        await store.CompleteAsync(lease.ExecutionId, SuccessResult(usageTotal: 10, text: "c"), 500, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.Equal(consumed, (await Usage(tenant)).Consumed);
    }

    [PgGatedFact]
    public async Task Expired_and_malformed_results_are_not_replayed()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var key = Key();
        var request = Request(tenant, user, key);
        var lease = await store.ReserveAsync(request, "Groq", "test-model", 1, 100, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        await store.CompleteAsync(lease.ExecutionId, SuccessResult(text: "vigente"), 100, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await Run("update ged.ai_execution set result_expires_at=now()-interval '1 minute' where id=@id", new { id = lease.ExecutionId });

        var expired = await store.ReserveAsync(request, "Groq", "test-model", 1, 100, CancellationToken.None);
        Assert.False(expired.IsOwner);
        Assert.True(expired.ResultExpired);
        Assert.Null(expired.ExistingResult);
        Assert.Equal(lease.ExecutionId, expired.ExecutionId);

        await Run("update ged.ai_execution set result_expires_at=now()+interval '1 day', result_json='[]'::jsonb where id=@id", new { id = lease.ExecutionId });
        var malformedLease = await store.ReserveAsync(request, "Groq", "test-model", 1, 100, CancellationToken.None);
        Assert.False(malformedLease.IsOwner);
        Assert.True(malformedLease.ResultMalformed);
        Assert.Null(malformedLease.ExistingResult);

        var status = await store.GetExecutionAsync(tenant, user, lease.ExecutionId, CancellationToken.None);
        Assert.NotNull(status);
        Assert.True(status!.ResultMalformed);
        Assert.Null(status.Result);
    }

    [PgGatedFact]
    public async Task Timeout_before_provider_consumes_zero()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 500, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        await store.CompleteAsync(lease.ExecutionId, new AiResult(false, null, null, null, "Groq", "test-model", AiFailureKind.Timeout, "timeout local"), 500, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal("RemoteOutcomeUnknown", await One<string>("select state from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(new Monthly(0, 0), await Usage(tenant));
        Assert.Equal(0L, await One<long>("select settled_tokens from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
    }

    [PgGatedFact]
    public async Task Reached_provider_without_metering_settles_the_estimated_usage()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var lease = await store.ReserveAsync(Request(tenant, user, Key()), "Groq", "test-model", 1, 500, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        Assert.True(await store.MarkSentAsync(lease.ExecutionId, CancellationToken.None));
        // Provider reached the network: metering unknown, so the reservation settles as estimated usage.
        await store.CompleteAsync(lease.ExecutionId, new AiResult(false, null, null, null, "Groq", "test-model", AiFailureKind.Internal, "falha apos envio", ProviderReached: true), 500, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("Failed", await One<string>("select state from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
        Assert.Equal(new Monthly(0, 500), await Usage(tenant)); // metering unknown: charged the estimate
        Assert.True(await Scalar<bool>("select usage_estimated from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
    }

    [PgGatedFact]
    public async Task GetExecution_scopes_by_user_and_returns_sources_and_result_expiry()
    {
        var tenant = await SeedTenant(limit: 5000); var user = Guid.NewGuid(); var store = Store;
        var document = Guid.NewGuid(); var version = Guid.NewGuid();
        var request = Request(tenant, user, Key()) with { Sources = [new AiExecutionSource(document, version)] };
        var lease = await store.ReserveAsync(request, "Groq", "test-model", 1, 100, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(lease.ExecutionId, CancellationToken.None));
        await store.CompleteAsync(lease.ExecutionId, SuccessResult(text: "resumo final"), 100, TimeSpan.FromMilliseconds(10), CancellationToken.None);

        var status = await store.GetExecutionAsync(tenant, user, lease.ExecutionId, CancellationToken.None);
        Assert.NotNull(status);
        Assert.Equal("resumo final", status!.Result?.Text);
        Assert.InRange(status.ResultExpiresAt!.Value, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));
        Assert.Contains(new AiExecutionSource(document, version), status.Sources!);

        Assert.Null(await store.GetExecutionAsync(tenant, Guid.NewGuid(), lease.ExecutionId, CancellationToken.None)); // another user of the same tenant
        await Run("update ged.ai_execution set result_expires_at=now()-interval '1 day' where id=@id", new { id = lease.ExecutionId });
        var stale = await store.GetExecutionAsync(tenant, user, lease.ExecutionId, CancellationToken.None);
        Assert.NotNull(stale); Assert.Null(stale!.Result); // expired: status visible, private content gone
        Assert.Equal(0, await store.ExpireReservationsAsync(CancellationToken.None)); // a completed execution holds no active reservation; the worker only purges its stale result (checked next)
        Assert.Null(await Scalar<string>(null, "select result_json::text from ged.ai_execution where id=@id", new { id = lease.ExecutionId }));
    }

    [PgGatedFact]
    public async Task Recovery_health_counts_remote_unknown_expired_and_pending_expired_states()
    {
        var tenant = await SeedTenant(limit: 20000); var user = Guid.NewGuid(); var store = Store;
        var pending = await store.ReserveAsync(Request(tenant, user, Key(), instructions: "A"), "Groq", "test-model", 1, 100, CancellationToken.None);
        var sent = await store.ReserveAsync(Request(tenant, user, Key(), instructions: "B"), "Groq", "test-model", 1, 100, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(sent.ExecutionId, CancellationToken.None));
        Assert.True(await store.MarkSentAsync(sent.ExecutionId, CancellationToken.None));
        var done = await store.ReserveAsync(Request(tenant, user, Key(), instructions: "C"), "Groq", "test-model", 1, 100, CancellationToken.None);
        Assert.True(await store.MarkRunningAsync(done.ExecutionId, CancellationToken.None));
        await store.CompleteAsync(done.ExecutionId, SuccessResult(text: "ok"), 100, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await BackdateExpiration(pending.ExecutionId, sent.ExecutionId);
        await Run("update ged.ai_execution set result_expires_at=now()-interval '1 day' where id=@id", new { id = done.ExecutionId });

        Assert.Equal(2, await store.ExpireReservationsAsync(CancellationToken.None));
        Assert.Equal(new AiRecoveryHealth(1, 1, 0), await store.GetRecoveryHealthAsync(CancellationToken.None));
        Assert.Equal(0, await One<int>("select count(*) from ged.ai_execution where result_expires_at<now() and result_json is not null")); // stale result purged
        Assert.Equal(0, await store.ExpireReservationsAsync(CancellationToken.None)); // idempotent
        Assert.Equal(new AiRecoveryHealth(1, 1, 0), await store.GetRecoveryHealthAsync(CancellationToken.None));
    }

    [PgGatedFact]
    public async Task Policy_is_fail_closed_per_tenant_task_and_state()
    {
        var tenant = await SeedTenant(limit: 1000); var store = Store;
        var policy = await store.GetEffectivePolicyAsync(tenant, AiTask.Summarize, CancellationToken.None);
        Assert.NotNull(policy); Assert.Equal(1000L, policy!.MonthlyTokenLimit);
        Assert.Single(policy.Tasks); Assert.Equal(AiTask.Summarize, policy.Tasks.Single());
        Assert.Equal("test-model", policy.Models[AiTask.Summarize]);

        Assert.Null(await store.GetEffectivePolicyAsync(tenant, AiTask.AskCollection, CancellationToken.None)); // task not allowed
        Assert.Null(await store.GetEffectivePolicyAsync(Guid.NewGuid(), AiTask.Summarize, CancellationToken.None)); // unknown tenant
        await Run("update ged.ai_tenant_policy set enabled=false where tenant_id=@t", new { t = tenant });
        Assert.Null(await store.GetEffectivePolicyAsync(tenant, AiTask.Summarize, CancellationToken.None)); // disabled
    }

    private PostgresAiGovernanceStore Store => new(new NpgsqlConnectionFactory(PgGate.Dsn()));
    private static string Key() => Guid.NewGuid().ToString("N");

    private static AiRequest Request(Guid tenant, Guid user, string key, string instructions = "resuma o documento") =>
        new(tenant, user, AiTask.Summarize, instructions, [new AiContextItem("DOC-1/1", "conteudo do documento para resumo", "text/plain", null)], null, key);

    private static AiResult SuccessResult(long? usageTotal = null, string? text = null) =>
        new(true, text ?? "resumo", null, usageTotal is null ? null : new AiUsage(usageTotal.Value / 2, usageTotal.Value / 2 - usageTotal.Value / 2, usageTotal), "Groq", "test-model", AiFailureKind.None, null, null, ProviderReached: true);

    private async Task<Guid> SeedTenant(long limit, long revision = 1)
    {
        var tenant = Guid.NewGuid();
        await Run("insert into ged.tenant(id,name,code) values(@t,@n,@c)", new { t = tenant, n = "tenant teste", c = tenant.ToString("N") });
        await Run("""insert into ged.ai_tenant_policy(tenant_id,enabled,revision,allowed_tasks,allowed_providers,task_models,monthly_token_limit) values(@t,true,@rev,'["Summarize"]'::jsonb,'["Groq"]'::jsonb,'{"Summarize":"test-model"}'::jsonb,@limit)""", new { t = tenant, rev = revision, limit });
        return tenant;
    }

    private async Task BackdateExpiration(params Guid[] ids)
    {
        foreach (var id in ids) await Run("update ged.ai_execution set expires_at=now()-interval '1 minute' where id=@id", new { id });
    }

    private Task Run(string sql, object? p) => _conn.ExecuteAsync(sql, p);
    private Task<T> One<T>(string sql, object? p = null) => _conn.QuerySingleAsync<T>(sql, p);
    private Task<int> Scalar(string sql, object? p = null) => One<int>(sql, p);
    private Task<T> Scalar<T>(string sql, object? p = null) => One<T>(sql, p);
    private Task<T> Scalar<T>(string? unused, string sql, object p) => One<T>(sql, p);

    // Typed projection instead of a dynamic select-* dictionary (Dapper's AsDictionary resolves only through the runtime binder and was intermittently null).
    private sealed record ExecutionSnapshot(string State, long SettledTokens, bool UsageEstimated, long DurationMs, bool HasResultJson);

    private sealed record Monthly(long Reserved, long Consumed);

    private async Task<Monthly> Usage(Guid tenant)
    {
        var r = await _conn.QuerySingleOrDefaultAsync<(long r1, long r2)>("select reserved_tokens,consumed_tokens from ged.ai_monthly_usage where tenant_id=@t and period_start=date_trunc('month',now())::date", new { t = tenant });
        return new Monthly(r.r1, r.r2);
    }

    private record ReservationOutcome(AiExecutionLease? Lease, Exception? Exception);
}

/// <summary>Pure (no database) behavioral tests of the governed reservation sizing and fail-closed pre-network paths.</summary>
public sealed class GovernedDocumentAiGatewayReservationTests
{
    [Fact]
    public async Task Reservation_equals_input_estimate_plus_effective_max_output_and_stamps_real_send()
    {
        var store = new FakeStore(); var options = GroqOptions(maxOutputTokens: 1000);
        var gateway = Governed(store, options);
        var context = new AiContextItem("DOC-1/1", new string('a', 2_000), "text/plain", null);
        var instructions = new string('b', 3_000);
        var inputCharacters = instructions.Length + context.Reference.Length + context.Text.Length;

        using var keys = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        var result = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, instructions, [context]), default);
        Assert.True(result.Success);

        var expected = Math.Max(1L, (inputCharacters + 2L) / 3L) + 1000;
        Assert.Equal(expected, store.LastReservation); // effective (clamped) max output, not an option that may exceed it
        Assert.Equal(1, store.SentStamps); // sent_at stamped at the real hand-off to the provider
        Assert.NotNull(store.LastCompletion);
    }

    [Fact]
    public async Task Failures_before_the_network_reach_no_provider_and_settle_the_lease()
    {
        var store = new FakeStore();
        var options = GroqOptions(maxOutputTokens: 1000);
        options.Providers["Groq"].BaseUrl = "https://attacker.example/api"; // endpoint outside the trusted provider set
        using var keys = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        var result = await Governed(store, options).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);
        Assert.Equal(AiFailureKind.Disabled, result.Failure);
        Assert.False(result.ProviderReached); // nothing left this host: the governance store settles the lease at zero consumption
        // Quota control is deliberately fail-closed: the conservative reservation happens before the attempt...
        Assert.Equal(Math.Max(1L, ("resuma".Length + 2L) / 3L) + 1000, store.LastReservation);
        Assert.NotNull(store.LastCompletion); // ...and even a pre-network failure settles it through the same lease
    }

    [Fact]
    public async Task Expired_or_malformed_replay_does_not_call_the_provider()
    {
        var handler = new TextHandler();
        var store = new FakeStore { NextLease = new AiExecutionLease(Guid.NewGuid(), false, AiExecutionState.Completed, ResultExpired: true) };
        var options = GroqOptions(maxOutputTokens: 1000);
        using var keys = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        var gateway = new GovernedDocumentAiGateway(new DocumentAiGateway(new HttpClient(handler), Options.Create(options), NullLogger<DocumentAiGateway>.Instance), store);
        var expired = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(AiFailureKind.InvalidOutput, expired.Failure);
        Assert.Contains("expirou", expired.Limitation, StringComparison.OrdinalIgnoreCase);

        store.NextLease = new AiExecutionLease(Guid.NewGuid(), false, AiExecutionState.Completed, ResultMalformed: true);
        var malformed = await gateway.ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "resuma", []), default);
        Assert.Equal(0, handler.Calls);
        Assert.Contains("malformado", malformed.Limitation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MaxOutputTokens_is_clamped_to_the_safe_band_and_used_in_reservations()
    {
        var store = new FakeStore(); var options = GroqOptions(maxOutputTokens: 1);
        Assert.Equal(64, Governed(store, options).MaxOutputTokens);
        using var keys = AiKeyScope.Set("GROQ_API_KEY", "test-only");
        await Governed(store, options).ExecuteAsync(new AiRequest(Guid.NewGuid(), Guid.NewGuid(), AiTask.Summarize, "r", []), default);
        Assert.Equal(Math.Max(1L, (1L + 2L) / 3L) + 64, store.LastReservation); // 1 char input -> 1 + clamped 64

        var huge = GroqOptions(maxOutputTokens: 999_999);
        Assert.Equal(32768, Governed(new FakeStore(), huge).MaxOutputTokens);
    }

    private static DocumentAiOptions GroqOptions(int maxOutputTokens)
    {
        var options = new DocumentAiOptions { Enabled = true, Provider = "Groq", MaximumOutputTokens = maxOutputTokens };
        options.TaskModels["Summarize"] = "test-model";
        options.Providers["Groq"] = new AiProviderOptions { Enabled = true, BaseUrl = "https://api.groq.com/openai/v1", AllowedModels = ["test-model"], StructuredOutputModels = ["test-model"] };
        return options;
    }

    private static GovernedDocumentAiGateway Governed(FakeStore store, DocumentAiOptions options) =>
        new(new DocumentAiGateway(new HttpClient(new TextHandler()), Options.Create(options), NullLogger<DocumentAiGateway>.Instance), store);

    private sealed class TextHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"choices":[{"finish_reason":"stop","message":{"content":"resumo"}}]}""") });
        }
    }

    private sealed class FakeStore : IAiGovernanceStore
    {
        public long LastReservation { get; set; } = long.MinValue;
        public int SentStamps { get; private set; }
        public AiResult? LastCompletion { get; private set; }
        public Task<AiEffectivePolicy?> GetEffectivePolicyAsync(Guid tenantId, AiTask task, CancellationToken ct) =>
            Task.FromResult<AiEffectivePolicy?>(new AiEffectivePolicy(tenantId, 1, true, [task], ["Groq"], new Dictionary<AiTask, string> { [task] = "test-model" }, 1_000_000, 50_000, DateTimeOffset.UtcNow, 0, 0));
        public AiExecutionLease? NextLease { get; set; }
        public Task<AiExecutionLease> ReserveAsync(AiRequest request, string provider, string model, long policyRevision, long estimatedTokens, CancellationToken ct)
        {
            LastReservation = estimatedTokens;
            return Task.FromResult(NextLease ?? new AiExecutionLease(Guid.NewGuid(), true, AiExecutionState.Reserved));
        }
        public Task<bool> MarkRunningAsync(Guid executionId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> MarkSentAsync(Guid executionId, CancellationToken ct) { SentStamps++; return Task.FromResult(true); }
        public Task CompleteAsync(Guid executionId, AiResult result, long reservedTokens, TimeSpan duration, CancellationToken ct) { LastCompletion = result; return Task.CompletedTask; }
        public Task<AiExecutionStatus?> GetExecutionAsync(Guid tenantId, Guid userId, Guid executionId, CancellationToken ct) => Task.FromResult<AiExecutionStatus?>(null);
        public Task MigrateVerifiedLegacySourcesAsync(Guid tenantId, Guid userId, Guid executionId, IReadOnlyList<AiExecutionSource> sources, CancellationToken ct) => Task.CompletedTask;
        public Task<int> ExpireReservationsAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<AiRecoveryHealth> GetRecoveryHealthAsync(CancellationToken ct) => Task.FromResult(new AiRecoveryHealth(0, 0, 0));
    }

    private sealed class AiKeyScope : IDisposable
    {
        private readonly string _name; private readonly string? _previous;
        private AiKeyScope(string name, string value) { _name = name; _previous = System.Environment.GetEnvironmentVariable(name); System.Environment.SetEnvironmentVariable(name, value); }
        public static AiKeyScope Set(string name, string value) => new(name, value);
        public void Dispose() => System.Environment.SetEnvironmentVariable(_name, _previous);
    }
}
