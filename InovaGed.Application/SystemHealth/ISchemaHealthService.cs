namespace InovaGed.Application.SystemHealth;

public interface ISchemaHealthService
{
    Task<SchemaHealthReportDto> CheckAsync(CancellationToken ct);
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
    public string Area { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string CheckType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Crítico";
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FixSuggestion { get; set; } = string.Empty;
}
