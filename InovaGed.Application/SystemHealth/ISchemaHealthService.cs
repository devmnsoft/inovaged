namespace InovaGed.Application.SystemHealth;

public interface ISchemaHealthService
{
    Task<SchemaHealthReportDto> CheckAsync(CancellationToken ct);
}

public interface ISchemaFixSqlProvider
{
    Task<IReadOnlyList<SchemaFixDto>> GetFixesAsync(SchemaHealthReportDto report, CancellationToken ct);
    bool TryGetKnownFix(string checkId, out SchemaFixDto fix);
}

public interface ISchemaRepairService
{
    Task<SchemaRepairResultDto> ApplyFixAsync(string checkId, string confirmation, Guid userId, CancellationToken ct);
    Task<SchemaRepairResultDto> ApplySafeFixesAsync(string confirmation, Guid userId, CancellationToken ct);
    Task<string> GenerateFixScriptAsync(CancellationToken ct);
    Task<SchemaFixPreflightResult> ValidateFixAsync(SchemaFixDto fix, CancellationToken ct);
}

public sealed class SchemaHealthReportDto
{
    public bool IsHealthy { get; set; }
    public List<SchemaCheckItemDto> Checks { get; set; } = new();
    public List<string> MissingTables { get; set; } = new();
    public List<string> MissingColumns { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public SchemaMigrationHistoryDto? LastMigration { get; set; }
}

public sealed class SchemaMigrationHistoryDto
{
    public string ScriptName { get; set; } = string.Empty;
    public DateTimeOffset AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
    public bool Success { get; set; }
    public string? Notes { get; set; }
}

public sealed class SchemaCheckItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string CheckType { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Severity { get; set; } = "Critical";
    public string Message { get; set; } = string.Empty;
    public string FixSuggestion { get; set; } = string.Empty;

    public bool CanAutoFix { get; set; }
    public string? FixSql { get; set; }
    public string? FixScriptName { get; set; }
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public string RiskLevel { get; set; } = "Low";
}

public sealed class SchemaFixDto
{
    public string CheckId { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string FixSql { get; set; } = string.Empty;
    public bool CanAutoFix { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public string FixType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ScriptName { get; set; } = string.Empty;
    public List<SchemaObjectDependency> Dependencies { get; set; } = new();
}

public sealed class SchemaObjectDependency
{
    public string Type { get; set; } = string.Empty;
    public string Schema { get; set; } = "ged";
    public string Table { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
}

public sealed class SchemaFixPreflightResult
{
    public bool CanRun { get; set; }
    public bool AlreadyApplied { get; set; }
    public bool ShouldSkip { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> MissingDependencies { get; set; } = new();
    public string? SafeSqlToRun { get; set; }
}

public sealed class SchemaRepairResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? CheckId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public int AppliedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<SchemaRepairItemResultDto> Items { get; set; } = new();
    public SchemaHealthReportDto? Report { get; set; }
}

public sealed class SchemaRepairItemResultDto
{
    public string CheckId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class SchemaRepairOptions
{
    public bool Enabled { get; set; } = true;
    public bool AllowApplyInProduction { get; set; }
    public bool RequireConfirmationText { get; set; } = true;
    public string ConfirmationText { get; set; } = "APLICAR CORRECAO";
    public bool CreateBackupRecommendation { get; set; } = true;
}
