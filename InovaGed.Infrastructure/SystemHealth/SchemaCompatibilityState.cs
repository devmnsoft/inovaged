using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed class SchemaCompatibilityState : ISchemaCompatibilityState
{
    private readonly ILogger<SchemaCompatibilityState> _logger;
    private readonly object _sync = new();
    private List<string> _missingTables = new();
    private List<string> _missingColumns = new();

    public SchemaCompatibilityState(ILogger<SchemaCompatibilityState> logger)
    {
        _logger = logger;
    }

    public bool HasStartupValidationRun { get; private set; }
    public bool IsCompatible { get; private set; } = true;
    public bool WorkersDisabled { get; private set; }
    public string? LastError { get; private set; }

    public IReadOnlyList<string> MissingTables
    {
        get { lock (_sync) return _missingTables.ToArray(); }
    }

    public IReadOnlyList<string> MissingColumns
    {
        get { lock (_sync) return _missingColumns.ToArray(); }
    }

    public void MarkCompatible()
    {
        lock (_sync)
        {
            HasStartupValidationRun = true;
            IsCompatible = true;
            WorkersDisabled = false;
            LastError = null;
            _missingTables = new List<string>();
            _missingColumns = new List<string>();
        }
    }

    public void MarkInvalid(IEnumerable<string> missingTables, IEnumerable<string> missingColumns, string? error, bool workersDisabled)
    {
        lock (_sync)
        {
            HasStartupValidationRun = true;
            IsCompatible = false;
            WorkersDisabled = workersDisabled;
            LastError = error;
            _missingTables = missingTables.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            _missingColumns = missingColumns.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public Task<bool> IsCompatibleAsync(string moduleName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (IsCompatible || !WorkersDisabled)
            return Task.FromResult(true);

        _logger.LogWarning(
            "{ModuleName} não iniciado: schema incompatível. Execute database/apply_all_required_migrations.sql. MissingTables=[{MissingTables}] MissingColumns=[{MissingColumns}]",
            moduleName,
            string.Join(", ", MissingTables),
            string.Join(", ", MissingColumns));
        return Task.FromResult(false);
    }
}
