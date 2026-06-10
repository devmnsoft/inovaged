using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed class SchemaCompatibilityState : ISchemaCompatibilityState
{
    private readonly ILogger<SchemaCompatibilityState> _logger;
    private readonly object _sync = new();
    private List<string> _missingTables = new();
    private List<string> _missingColumns = new();

    private static readonly Dictionary<string, ModuleSchemaDependency> ModuleDependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OcrAutoSchedule"] = new(
            ["ged.ocr_auto_schedule_run", "ged.ocr_auto_schedule_run_item"],
            ["ged.ocr_auto_schedule_run.tenant_id", "ged.ocr_auto_schedule_run.started_at_utc", "ged.ocr_auto_schedule_run.status", "ged.ocr_auto_schedule_run_item.run_id", "ged.ocr_auto_schedule_run_item.status"]),
        ["OCR"] = new(
            ["ged.ocr_job", "ged.document_version", "ged.document"],
            ["ged.ocr_job.document_version_id", "ged.ocr_job.status", "ged.document.id", "ged.document_version.id"]),
        ["OcrWorker"] = new(
            ["ged.ocr_job", "ged.document_version", "ged.document"],
            ["ged.ocr_job.document_version_id", "ged.ocr_job.status", "ged.document.id", "ged.document_version.id"]),
        ["UploadBatch"] = new(
            ["ged.upload_batch", "ged.upload_batch_item", "ged.upload_session"],
            ["ged.upload_batch.tenant_id", "ged.upload_batch.status", "ged.upload_batch_item.batch_id", "ged.upload_batch_item.status", "ged.upload_session.tenant_id"]),
        ["LoanOverdue"] = new(
            ["ged.loan_request", "ged.loan_request_item"],
            ["ged.loan_request.id", "ged.loan_request.status", "ged.loan_request_item.loan_request_id", "ged.loan_request_item.description"]),
        ["Retention"] = new(
            ["ged.retention_rule", "ged.retention_event"],
            []),
        ["SystemSeed"] = new(
            ["ged.app_user", "ged.app_role", "ged.user_role"],
            ["ged.app_user.id", "ged.app_role.normalized_name"])
    };

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

    public bool IsModuleCompatible(string moduleName)
    {
        if (IsCompatible || !WorkersDisabled)
            return true;

        if (!ModuleDependencies.TryGetValue(moduleName, out var dependency))
            return false;

        var missingTables = MissingTables;
        var missingColumns = MissingColumns;
        return !missingTables.Any(t => dependency.Tables.Contains(t))
            && !missingColumns.Any(c => dependency.Columns.Contains(c));
    }

    public Task<bool> IsCompatibleAsync(string moduleName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (IsModuleCompatible(moduleName))
            return Task.FromResult(true);

        _logger.LogWarning(
            "{ModuleName} não iniciado: schema incompatível para o módulo. Execute database/apply_all_required_migrations.sql. MissingTables=[{MissingTables}] MissingColumns=[{MissingColumns}]",
            moduleName,
            string.Join(", ", MissingTables),
            string.Join(", ", MissingColumns));
        return Task.FromResult(false);
    }

    private sealed record ModuleSchemaDependency(HashSet<string> Tables, HashSet<string> Columns)
    {
        public ModuleSchemaDependency(IEnumerable<string> tables, IEnumerable<string> columns)
            : this(tables.ToHashSet(StringComparer.OrdinalIgnoreCase), columns.ToHashSet(StringComparer.OrdinalIgnoreCase))
        {
        }
    }
}
