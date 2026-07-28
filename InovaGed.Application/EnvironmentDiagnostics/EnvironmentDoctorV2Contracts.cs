using System.Runtime.InteropServices;

namespace InovaGed.Application.EnvironmentDiagnostics;

public interface IEnvironmentContext
{
    string CurrentDirectory { get; }
    string BaseDirectory { get; }
    string MachineName { get; }
    string OperatingSystemDescription { get; }
    Architecture ProcessArchitecture { get; }
    Architecture OperatingSystemArchitecture { get; }
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }
    string? GetEnvironmentVariable(string name);
}

public interface IProcessRunner
{
    Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken);
}

public sealed record ProcessExecutionRequest(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout,
    string? WorkingDirectory = null, IReadOnlyDictionary<string, string?>? EnvironmentVariables = null);

public sealed record ProcessExecutionResult(bool Started, bool TimedOut, int? ExitCode, string StandardOutput,
    string StandardError, TimeSpan Duration, string? FailureCode);

public sealed record EnvironmentCheckContext(string RepositoryRoot, string ApplicationVersion, string EnvironmentName,
    bool IncludeOptionalChecks, bool IsProduction, string CorrelationId);

public interface IEnvironmentProbe
{
    string Code { get; }
    string Category { get; }
    int Order { get; }
    Task<EnvironmentCheckResult> CheckAsync(EnvironmentCheckContext context, CancellationToken cancellationToken);
}

public sealed record RepositoryRootResult(bool Found, string? RootPath, string ResolutionSource,
    IReadOnlyList<string> Evidence);

public interface IRepositoryRootLocator
{
    RepositoryRootResult Locate(string? explicitPath = null);
}

public interface ISafeMetadataSanitizer
{
    IReadOnlyDictionary<string, string?> Sanitize(IReadOnlyDictionary<string, string?> metadata);
    string SanitizeText(string value);
}

public sealed record DoctorCommandOptions(string Command, string Profile, bool Json, bool FailOnWarning,
    string? OutputDirectory, string? ExplainCode);
