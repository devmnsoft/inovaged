namespace InovaGed.Application.SystemHealth;

public enum StartupConfigurationSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record StartupConfigurationCheck(
    string Item,
    string Status,
    StartupConfigurationSeverity Severity,
    string MaskedValue,
    string Recommendation,
    string Source,
    string Environment)
{
    public string Module { get; init; } = "Startup";
    public bool BlocksStartup => Severity == StartupConfigurationSeverity.Critical;
    public DateTimeOffset LastValidatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public interface IStartupConfigurationValidator
{
    IReadOnlyList<StartupConfigurationCheck> Validate();
}

public interface ISecretMasker
{
    string Mask(string? value);
}

public interface IApplicationClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ITenantTimeZoneProvider
{
    string GetTimeZoneId(Guid? tenantId = null);
}

public interface IDateTimeZoneConverter
{
    DateTimeOffset ToTenantLocal(DateTimeOffset utc, Guid? tenantId = null);
}

public interface IExecutableResolver
{
    ExecutableResolution Resolve(string toolName, string? configuredPath, string environmentVariable, IReadOnlyList<string> knownPaths);
}

public sealed record ExecutableResolution(string ToolName, bool IsAvailable, string? Path, string Source, string Message);

public interface ITenantCatalog
{
    Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken);
}

public interface ITenantExecutionContext
{
    Guid TenantId { get; }
    string CorrelationId { get; }
}

public interface IJobExecutionLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(Guid tenantId, string jobName, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface ISystemUserProvider
{
    Guid GetSystemUserId(Guid tenantId);
    string GetSystemUserName(Guid tenantId);
}
