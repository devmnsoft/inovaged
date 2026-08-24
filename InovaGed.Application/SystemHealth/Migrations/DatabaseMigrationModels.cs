namespace InovaGed.Application.SystemHealth.Migrations;

public interface IDatabaseMigrationRunner
{
    Task<DatabaseMigrationPlan> GetPlanAsync(CancellationToken ct);
    Task<DatabaseMigrationResult> ApplyRequiredAsync(Guid? userId, string? userName, CancellationToken ct);
    Task<DatabaseMigrationResult> ApplyOneAsync(string migrationName, Guid? userId, string? userName, CancellationToken ct);
    Task<string> GetConsolidatedPendingScriptAsync(CancellationToken ct);
}

public sealed record DatabaseMigrationPlan(IReadOnlyList<DatabaseMigrationPlanItem> Items, int Total, int Applied, int Pending, int Failed);
public sealed record DatabaseMigrationPlanItem(string Name, string Path, string Area, string Description, bool Required, bool Applied, bool FailedBefore, string? ChecksumSha256, DateTimeOffset? AppliedAt, string? LastError);
public sealed record DatabaseMigrationResult(bool Success, string Message, IReadOnlyList<DatabaseMigrationExecutionItem> Items);
public sealed record DatabaseMigrationExecutionItem(string Name, string Path, bool Success, int DurationMs, string? ErrorMessage);
