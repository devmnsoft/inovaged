namespace InovaGed.Application.SystemHealth;

public interface ISchemaCompatibilityState
{
    bool HasStartupValidationRun { get; }
    bool IsCompatible { get; }
    bool WorkersDisabled { get; }
    string? LastError { get; }
    IReadOnlyList<string> MissingTables { get; }
    IReadOnlyList<string> MissingColumns { get; }
    void MarkCompatible();
    void MarkInvalid(IEnumerable<string> missingTables, IEnumerable<string> missingColumns, string? error, bool workersDisabled);
    bool IsModuleCompatible(string moduleName);
    Task<bool> IsCompatibleAsync(string moduleName, CancellationToken ct);
}
