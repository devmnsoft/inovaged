using System.Data;
using System.Data.Common;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using Npgsql;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

/// <summary>One transaction for the canonical change, the review record and the audit.</summary>
internal static class AssistedApplicationWrite
{
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

        var existingId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
select id from ged.ai_suggestion_application
where tenant_id=@TenantId and execution_id=@ExecutionId and decision_fingerprint=@DecisionFingerprint
""", new { application.TenantId, application.ExecutionId, application.DecisionFingerprint }, transaction, cancellationToken: ct));
        if (existingId is not null)
        {
            await transaction.CommitAsync(ct);
            return new("AlreadyApplied", "Esta revisão já foi registrada. Nenhuma auditoria ou gravação adicional foi feita.", locked.Token, existingId);
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
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_suggestion_application(id,tenant_id,execution_id,document_id,version_id,task,reviewer_id,decision_fingerprint,decision_json,outcome,partial)
values(@Id,@TenantId,@ExecutionId,@DocumentId,@VersionId,@Task,@ReviewerId,@DecisionFingerprint,cast(@DecisionJson as jsonb),@Outcome,@Partial)
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
                application.Partial
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

            var token = await connection.ExecuteScalarAsync<long>(new CommandDefinition("select xmin::text::bigint from ged.document where tenant_id=@TenantId and id=@DocumentId", new { application.TenantId, application.DocumentId }, transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
            return new(mutate ? "Applied" : "Recorded", null, token, applicationId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct);
            return new("AlreadyApplied", "Esta revisão já foi registrada. Nenhuma auditoria ou gravação adicional foi feita.", locked.Token, null);
        }
    }

    private sealed class LockRow { public Guid Id { get; set; } public long Token { get; set; } }
}
