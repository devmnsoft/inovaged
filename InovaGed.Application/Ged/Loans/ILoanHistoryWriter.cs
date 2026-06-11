namespace InovaGed.Application.Ged.Loans;

public interface ILoanHistoryWriter
{
    Task WriteAsync(
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
        CancellationToken ct);
}
