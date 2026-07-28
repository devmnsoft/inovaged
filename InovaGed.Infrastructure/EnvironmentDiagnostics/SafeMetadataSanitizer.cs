using System.Text.RegularExpressions;
using InovaGed.Application.EnvironmentDiagnostics;

namespace InovaGed.Infrastructure.EnvironmentDiagnostics;

public sealed partial class SafeMetadataSanitizer : ISafeMetadataSanitizer
{
    private static readonly string[] Blocked = ["password", "pwd", "secret", "token", "key", "connectionstring", "cookie", "authorization", "private", "cpf", "patient"];
    public IReadOnlyDictionary<string, string?> Sanitize(IReadOnlyDictionary<string, string?> metadata) => metadata
        .Where(x => !Blocked.Any(blocked => x.Key.Contains(blocked, StringComparison.OrdinalIgnoreCase)))
        .ToDictionary(x => x.Key, x => x.Value is null ? null : SanitizeText(x.Value), StringComparer.OrdinalIgnoreCase);
    public string SanitizeText(string value) => UnixPath().Replace(WindowsPath().Replace(Credentials().Replace(value, "$1=[REDACTED]"), "[PATH]"), "[PATH]");
    [GeneratedRegex(@"(?i)(password|pwd|secret|token|authorization|connectionstrings?|privatekey|cpf)\s*=\s*[^;\s]+")]
    private static partial Regex Credentials();
    [GeneratedRegex(@"[A-Za-z]:\\[^\r\n\s]+")]
    private static partial Regex WindowsPath();
    [GeneratedRegex(@"(?<!\w)/(?:home|root|workspace|var|tmp)/[^\r\n\s]+")]
    private static partial Regex UnixPath();
}
