using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanCollectionService : ILoanCollectionService
{
    private const int MaximumMessageLength = 2_000;

    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<LoanCollectionService> _logger;

    public LoanCollectionService(
        IDbConnectionFactory db,
        IAuditWriter audit,
        ILogger<LoanCollectionService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result> CollectAsync(
        Guid tenantId,
        Guid loanId,
        Guid? actorId,
        string? message,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            return Result.Fail("INVALID_TENANT", "Tenant inválido.");

        if (loanId == Guid.Empty)
            return Result.Fail("INVALID_LOAN", "Empréstimo inválido.");

        var normalizedMessage = NormalizeMessage(message);
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            const string selectLoanSql = """
select status::text as Status,
       coalesce(collection_count, 0) as CollectionCount
from ged.loan_request l
where l.tenant_id = @TenantId
  and l.id = @LoanId
  and coalesce(l.reg_status, 'A') = 'A'
for update;
""";
            var loan = await conn.QuerySingleOrDefaultAsync<LoanCollectionState>(
                new CommandDefinition(
                    selectLoanSql,
                    new { TenantId = tenantId, LoanId = loanId },
                    transaction: tx,
                    cancellationToken: ct));

            if (loan is null)
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("LOAN_NOT_FOUND", "Empréstimo não encontrado.");
            }

            var status = loan.Status.ToUpperInvariant();
            if (status is "RETURNED" or "REJECTED" or "CANCELLED" or "CANCELED")
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("LOAN_CLOSED", "Não é possível cobrar um empréstimo encerrado.");
            }

            if (!IsCollectableStatus(status))
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("LOAN_STATUS_NOT_COLLECTABLE", "O estado atual do empréstimo não permite cobrança.");
            }

            var collectionLevel = GetCollectionLevel(loan.CollectionCount);
            normalizedMessage ??= $"Solicitamos a devolução do empréstimo. Nível: {collectionLevel}.";

            const string insertEventSql = """
insert into ged.loan_collection_event
(
    tenant_id, loan_id, loan_request_id, event_at, created_at,
    kind, event_type, level, channel, delivery_status,
    message, created_by, reg_status
)
values
(
    @TenantId, @LoanId, @LoanId, now(), now(),
    'MANUAL_COLLECTION', 'MANUAL_COLLECTION', @CollectionLevel, 'INTERNAL', 'PENDING_EXTERNAL',
    @Message, @ActorId, 'A'
);
""";
            var eventRows = await conn.ExecuteAsync(
                new CommandDefinition(
                    insertEventSql,
                    new
                    {
                        TenantId = tenantId,
                        LoanId = loanId,
                        CollectionLevel = collectionLevel,
                        Message = normalizedMessage,
                        ActorId = actorId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            const string updateLoanSql = """
update ged.loan_request l
set collection_count = coalesce(collection_count, 0) + 1,
    last_collection_at = now(),
    collection_level = @CollectionLevel,
    updated_at = now(),
    updated_by = @ActorId
where l.tenant_id = @TenantId
  and l.id = @LoanId
  and coalesce(l.reg_status, 'A') = 'A';
""";
            var affectedRows = await conn.ExecuteAsync(
                new CommandDefinition(
                    updateLoanSql,
                    new { TenantId = tenantId, LoanId = loanId, CollectionLevel = collectionLevel, ActorId = actorId },
                    transaction: tx,
                    cancellationToken: ct));

            const string insertHistorySql = """
insert into ged.loan_request_history
(
    tenant_id, loan_request_id, old_status, new_status, action,
    user_id, user_name, reason, internal_notes, metadata_json,
    correlation_id, created_at, reg_status
)
values
(
    @TenantId, @LoanId, @Status, @Status, 'LOAN_COLLECTION',
    @ActorId, 'Sistema de Cobrança', @Message, 'Envio externo pendente',
    jsonb_build_object('level', @CollectionLevel, 'count', @CollectionCount, 'channel', 'INTERNAL'),
    @CorrelationId, now(), 'A'
);
""";
            var historyRows = await conn.ExecuteAsync(
                new CommandDefinition(
                    insertHistorySql,
                    new
                    {
                        TenantId = tenantId,
                        LoanId = loanId,
                        Status = status,
                        ActorId = actorId,
                        Message = normalizedMessage,
                        CollectionLevel = collectionLevel,
                        CollectionCount = loan.CollectionCount + 1,
                        CorrelationId = correlationId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            if (eventRows != 1 || affectedRows != 1 || historyRows != 1)
                throw new InvalidOperationException("A cobrança não atualizou todos os registros esperados.");

            await tx.CommitAsync(ct);

            var auditResult = await _audit.WriteAsync(
                new AuditWriteCommand(
                    TenantId: tenantId,
                    UserId: actorId,
                    Action: "LOAN_COLLECTION_REGISTERED",
                    EntityName: "loan_request",
                    EntityId: loanId,
                    Summary: "Cobrança interna registrada.",
                    IpAddress: null,
                    UserAgent: null,
                    CorrelationId: correlationId,
                    Data: new
                    {
                        loanId,
                        status,
                        collectionLevel,
                        collectionCount = loan.CollectionCount + 1,
                        channel = "INTERNAL",
                        deliveryStatus = "PENDING_EXTERNAL",
                        outcome = "SUCCESS"
                    },
                    EventType: "INFO",
                    Outcome: "SUCCESS"),
                ct);

            if (auditResult.IsFailure)
            {
                _logger.LogError(
                    "Auditoria da cobrança falhou. Tenant={TenantId} Loan={LoanId} Code={Code}",
                    tenantId,
                    loanId,
                    auditResult.Error?.Code);
                return Result.Fail("LOAN_COLLECTION_AUDIT_ERROR", "A cobrança foi registrada, mas sua auditoria não pôde ser confirmada.");
            }

            return Result.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao registrar cobrança. Tenant={TenantId} Loan={LoanId}",
                tenantId,
                loanId);

            return Result.Fail(
                "LOAN_COLLECTION_ERROR",
                "Não foi possível registrar a cobrança do empréstimo.");
        }
    }

    private static bool IsCollectableStatus(string status) => status is
        "APPROVED" or
        "DELIVERED" or
        "OVERDUE" or
        "PREPARING_PHYSICAL" or
        "WAITING_PICKUP" or
        "DIGITAL_LINK_SENT";

    private static string GetCollectionLevel(int collectionCount) => collectionCount switch
    {
        <= 0 => "FIRST_NOTICE",
        1 => "SECOND_NOTICE",
        2 => "ESCALATED",
        _ => "FINAL_NOTICE"
    };

    private static string? NormalizeMessage(string? message)
    {
        var normalized = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized.Length <= MaximumMessageLength
            ? normalized
            : normalized[..MaximumMessageLength];
    }

    private sealed class LoanCollectionState
    {
        public string Status { get; init; } = string.Empty;
        public int CollectionCount { get; init; }
    }
}
