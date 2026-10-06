using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Npgsql;

namespace InovaGed.Infrastructure.Retention;

public sealed record RetentionRecoveryResult(bool Resolved, int Attempts, string State);

/// <summary>
/// Uses the existing retention calculation with a database lock, not a time-based lease.
/// Calculation, pending completion and review completion commit together.
/// </summary>
public sealed class AssistedRetentionRecovery(IDbConnectionFactory db, RetentionRecalcService retention)
{
    public async Task<int> RunBatchAsync(Guid tenantId, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var ids = await connection.QueryAsync<Guid>(new CommandDefinition("""
select id from ged.ai_retention_recalc_pending
where tenant_id=@tenantId and resolved_at is null and attempts < 10
  and next_attempt_at <= now()
  and (claimed_at is null or claimed_at < now() - interval '90 seconds')
order by next_attempt_at, created_at, id limit 20
""", new { tenantId }, cancellationToken: ct));
        var resolved = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if ((await RunAsync(tenantId, id, false, ct)).Resolved) resolved++;
        }
        return resolved;
    }

    public async Task<RetentionRecoveryResult> RunAsync(Guid tenantId, Guid pendingId, bool manual, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var item = await connection.QuerySingleOrDefaultAsync<Pending>(new CommandDefinition("""
select document_id "DocumentId", application_id "ApplicationId", attempts "Attempts", resolved_at "ResolvedAt"
from ged.ai_retention_recalc_pending
where tenant_id=@tenantId and id=@pendingId
  and (resolved_at is not null or (
    (claimed_at is null or claimed_at < now() - interval '90 seconds')
    and (@manual or (attempts < 10 and next_attempt_at <= now()))))
for update skip locked
""", new { tenantId, pendingId, manual }, tx, cancellationToken: ct));
        if (item is null) return new(false, 0, "pendente");
        if (item.ResolvedAt is not null) return new(true, item.Attempts, "resolvida");

        await connection.ExecuteAsync(new CommandDefinition("""
update ged.ai_retention_recalc_pending set attempts=attempts+1
where tenant_id=@tenantId and id=@pendingId
""", new { tenantId, pendingId }, tx, cancellationToken: ct));
        await tx.SaveAsync("recalculate", ct);
        try
        {
            // The row lock survives arbitrarily long calculations. On process/connection loss,
            // PostgreSQL rolls back every effect and makes the durable pending item available.
            var rows = await retention.RunOneAsync(connection, tx, tenantId, item.DocumentId, 30, ct);
            if (rows != 1) throw new InvalidOperationException("retention_document_missing");
            if (item.ApplicationId is Guid applicationId)
            {
                var changed = await connection.ExecuteAsync(new CommandDefinition("""
update ged.ai_suggestion_application set partial=false
where tenant_id=@tenantId and id=@applicationId and document_id=@DocumentId
""", new { tenantId, applicationId, item.DocumentId }, tx, cancellationToken: ct));
                if (changed != 1) throw new InvalidOperationException("retention_review_missing");
            }
            await connection.ExecuteAsync(new CommandDefinition("""
update ged.ai_retention_recalc_pending
set resolved_at=now(), claimed_at=null, claim_token=null, last_error=null
where tenant_id=@tenantId and id=@pendingId
""", new { tenantId, pendingId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return new(true, item.Attempts + 1, item.Attempts > 0 ? "recuperada" : "concluida");
        }
        catch (Exception ex)
        {
            // Roll back calculation and BOTH completion writes, retaining this attempt only.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await tx.RollbackAsync("recalculate", cleanup.Token);
            var error = ex is OperationCanceledException ? "temporalidade:cancelada"
                : ex is PostgresException pg ? "temporalidade:" + pg.SqlState : "temporalidade:falha";
            await connection.ExecuteAsync(new CommandDefinition("""
update ged.ai_retention_recalc_pending
set last_error=@error, claimed_at=null, claim_token=null,
    next_attempt_at=now() + make_interval(secs => least(3600, 30 * power(2, least(attempts, 7))))
where tenant_id=@tenantId and id=@pendingId
""", new { tenantId, pendingId, error }, tx, cancellationToken: cleanup.Token));
            await tx.CommitAsync(cleanup.Token);
            if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
            return new(false, item.Attempts + 1, item.Attempts + 1 >= 10 ? "intervencao necessaria" : "pendente");
        }
    }

    private sealed class Pending
    {
        public Guid DocumentId { get; set; }
        public Guid? ApplicationId { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
    }
}
