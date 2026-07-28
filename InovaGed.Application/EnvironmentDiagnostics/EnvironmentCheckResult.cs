namespace InovaGed.Application.EnvironmentDiagnostics;

public sealed record EnvironmentCheckResult(
    string Code,
    string Category,
    string Status,
    string Title,
    string Message,
    string? Recommendation,
    bool Blocking,
    IReadOnlyDictionary<string, string?> SafeMetadata);

public static class EnvironmentCheckStatuses
{
    public const string Pass = "PASS";
    public const string Warning = "WARNING";
    public const string Fail = "FAIL";
    public const string NotApplicable = "NOT_APPLICABLE";
    public const string NotVerifiable = "NOT_VERIFIABLE";
}
