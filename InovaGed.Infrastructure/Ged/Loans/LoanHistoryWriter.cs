using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanHistoryWriter : ILoanHistoryWriter
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<LoanHistoryWriter> _logger;

    public LoanHistoryWriter(IDbConnectionFactory db, ILogger<LoanHistoryWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task WriteAsync(
        Guid tenantId,
        Guid loanRequestId,
        string action,
        string? oldStatus,
        string? newStatus,
        Guid? userId,
        string? userName,
        Guid? sectorId,
        string? sectorName,
        string? reason,
        string? internalNotes,
        object? metadata,
        string? correlationId,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var historyExists = await conn.ExecuteScalarAsync<string?>(
                new CommandDefinition("select to_regclass('ged.loan_request_history')::text", cancellationToken: ct));

            if (string.IsNullOrWhiteSpace(historyExists))
            {
                _logger.LogWarning(
                    "Histórico de Loans não configurado. Ignorando registro de histórico. Tenant={TenantId} Loan={LoanRequestId} Action={Action}",
                    tenantId,
                    loanRequestId,
                    action);
                return;
            }

            const string sql = """
insert into ged.loan_request_history
(tenant_id, loan_request_id, old_status, new_status, action, user_id, user_name, sector_id, sector_name, reason, internal_notes, metadata_json, correlation_id, created_at, reg_status)
values
(@TenantId, @LoanRequestId, @OldStatus, @NewStatus, @Action, @UserId, @UserName, @SectorId, @SectorName, @Reason, @InternalNotes, (@MetadataJson)::jsonb, @CorrelationId, now(), 'A');
""";
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                LoanRequestId = loanRequestId,
                OldStatus = TrimOrNull(oldStatus),
                NewStatus = TrimOrNull(newStatus),
                Action = TrimOrNull(action) ?? "LOAN_EVENT",
                UserId = userId,
                UserName = TrimOrNull(userName) ?? "Sistema",
                SectorId = sectorId,
                SectorName = TrimOrNull(sectorName),
                Reason = TrimOrNull(reason),
                InternalNotes = TrimOrNull(internalNotes),
                MetadataJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata),
                CorrelationId = TrimOrNull(correlationId) ?? Guid.NewGuid().ToString("N")
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gravar histórico rico de empréstimo. Fluxo principal preservado. Tenant={TenantId} Loan={LoanRequestId} Action={Action}", tenantId, loanRequestId, action);
        }
    }

    private static string? TrimOrNull(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
