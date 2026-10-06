using System.Data;
using System.Data.Common;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using Npgsql;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

/// <summary>One transaction for the canonical change, the review, the audit and the durable retention work.</summary>
internal static class AssistedApplicationWrite
{
    private const string PendingReason = "Recálculo de temporalidade pendente após a gravação da classificação.";

    public static async Task<AssistedWriteResult> ExecuteAsync(
        IDbConnection connection,
        AssistedApplicationRecord application,
        long concurrencyToken,
        bool mutate,
        Func<IDbConnection, IDbTransaction, Task<int>>? mutateAsync,
        CancellationToken ct)
    {
        var database = (DbConnection)connection;
        await using var transaction = await database.BeginTransactionAsync(ct);
        var locked = await connection.QuerySingleOrDefaultAsync<LockRow>(new CommandDefinition("""
select id "Id", xmin::text::bigint "Token"
from ged.document
where tenant_id=@TenantId and id=@DocumentId and coalesce(reg_status,'A')='A'
for update
""", new { application.TenantId, application.DocumentId }, transaction, cancellationToken: ct));
        if (locked is null) { await transaction.RollbackAsync(ct); return new("NotFound", "Documento não encontrado.", null, null); }

        var existing = await FindAsync(connection, transaction, application, ct);
        if (existing is not null)
        {
            var replay = await ReplayAsync(connection, transaction, application, existing, locked.Token, ct);
            if (replay.Code == "AlreadyApplied") await transaction.CommitAsync(ct);
            else await transaction.RollbackAsync(ct);
            return replay;
        }

        if (mutate)
        {
            if (locked.Token != concurrencyToken)
            {
                await transaction.RollbackAsync(ct);
                return new("Conflict", "O documento foi alterado por outra edição. Recarregue antes de aplicar.", locked.Token, null);
            }
            var affected = mutateAsync is null ? 0 : await mutateAsync(connection, transaction);
            if (affected == 0)
            {
                await transaction.RollbackAsync(ct);
                return new("Conflict", "O documento mudou durante a gravação. Recarregue antes de aplicar.", locked.Token, null);
            }
        }

        var applicationId = Guid.NewGuid();
        var pendingId = application.QueueRetention && mutate ? Guid.NewGuid() : (Guid?)null;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_suggestion_application(id,tenant_id,execution_id,document_id,version_id,task,reviewer_id,decision_fingerprint,decision_json,outcome,partial,operation_key)
values(@Id,@TenantId,@ExecutionId,@DocumentId,@VersionId,@Task,@ReviewerId,@DecisionFingerprint,cast(@DecisionJson as jsonb),@Outcome,@Partial,@OperationKey)
""", new
            {
                Id = applicationId,
                application.TenantId,
                application.ExecutionId,
                application.DocumentId,
                application.VersionId,
                application.Task,
                application.ReviewerId,
                application.DecisionFingerprint,
                application.DecisionJson,
                application.Outcome,
                Partial = pendingId is not null,
                application.OperationKey
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.app_audit_log(id,tenant_id,user_id,user_name,action,event_type,source,entity_name,entity_id,message,details,correlation_id,ip_address,user_agent,created_at,reg_status)
values(gen_random_uuid(),@TenantId,@UserId,'',@Action,'INFO','DocumentAiAssist','DOCUMENT',@EntityId,@Message,cast(@Details as jsonb),@CorrelationId,@Ip,@UserAgent,now(),'A')
""", new
            {
                application.TenantId,
                UserId = application.ReviewerId,
                Action = application.AuditAction,
                EntityId = application.DocumentId.ToString(),
                Details = application.AuditDetailsJson,
                application.CorrelationId,
                Ip = string.IsNullOrWhiteSpace(application.Ip) ? null : application.Ip,
                UserAgent = application.UserAgent,
                Message = application.AuditMessage
            }, transaction, cancellationToken: ct));

            if (pendingId is Guid durableId)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_retention_recalc_pending(id,tenant_id,document_id,application_id,reason,attempts)
values(@Id,@TenantId,@DocumentId,@ApplicationId,@Reason,0)
""", new { Id = durableId, application.TenantId, application.DocumentId, ApplicationId = applicationId, Reason = PendingReason }, transaction, cancellationToken: ct));
            }

            var token = await connection.ExecuteScalarAsync<long>(new CommandDefinition("select xmin::text::bigint from ged.document where tenant_id=@TenantId and id=@DocumentId", new { application.TenantId, application.DocumentId }, transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
            return new(mutate ? "Applied" : "Recorded", null, token, applicationId, pendingId is not null, pendingId is not null, pendingId, mutate ? "Applied" : "Recorded");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct);
            var raced = await FindAsync(connection, null, application, ct);
            if (raced is null) return new("AlreadyApplied", "Esta revisão já foi registrada. Nenhuma auditoria ou gravação adicional foi feita.", locked.Token, null);
            return await ReplayAsync(connection, null, application, raced, locked.Token, ct);
        }
    }

    private static async Task<ExistingRow?> FindAsync(IDbConnection connection, IDbTransaction? transaction, AssistedApplicationRecord application, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(application.OperationKey))
        {
            var byKey = await connection.QuerySingleOrDefaultAsync<ExistingRow>(new CommandDefinition("""
select id "Id", decision_fingerprint "Fingerprint", decision_json::text "DecisionJson", outcome "Outcome", partial "Partial"
from ged.ai_suggestion_application
where tenant_id=@TenantId and operation_key=@OperationKey
""", new { application.TenantId, application.OperationKey }, transaction, cancellationToken: ct));
            if (byKey is not null) return byKey;
        }
        return await connection.QuerySingleOrDefaultAsync<ExistingRow>(new CommandDefinition("""
select id "Id", decision_fingerprint "Fingerprint", decision_json::text "DecisionJson", outcome "Outcome", partial "Partial"
from ged.ai_suggestion_application
where tenant_id=@TenantId and execution_id=@ExecutionId and decision_fingerprint=@DecisionFingerprint
""", new { application.TenantId, application.ExecutionId, application.DecisionFingerprint }, transaction, cancellationToken: ct));
    }

    private static async Task<AssistedWriteResult> ReplayAsync(IDbConnection connection, IDbTransaction? transaction, AssistedApplicationRecord application, ExistingRow existing, long token, CancellationToken ct)
    {
        if (!ReviewIdentity.Equivalent(existing.Fingerprint, existing.DecisionJson, application.DecisionFingerprint, application.DecisionJson))
            return new("DecisionConflict", "A mesma revisão já foi registrada com outra decisão. Gere uma nova sugestão para revisar de novo.", token, existing.Id, existing.Partial, false, null, existing.Outcome);
        var pending = await connection.QuerySingleOrDefaultAsync<PendingRow>(new CommandDefinition("""
select id "Id", attempts "Attempts", resolved_at is null "Open"
from ged.ai_retention_recalc_pending
where tenant_id=@TenantId and application_id=@ApplicationId
order by created_at desc
limit 1
""", new { application.TenantId, ApplicationId = existing.Id }, transaction, cancellationToken: ct));
        var open = pending?.Open == true;
        return new("AlreadyApplied", null, token, existing.Id, open, open, pending?.Id, existing.Outcome, pending?.Attempts ?? 0);
    }

    private sealed class LockRow { public Guid Id { get; set; } public long Token { get; set; } }
    private sealed class ExistingRow { public Guid Id { get; set; } public string Fingerprint { get; set; } = ""; public string DecisionJson { get; set; } = ""; public string Outcome { get; set; } = ""; public bool Partial { get; set; } }
    private sealed class PendingRow { public Guid Id { get; set; } public int Attempts { get; set; } public bool Open { get; set; } }
}
