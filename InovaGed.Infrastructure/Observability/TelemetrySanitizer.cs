using System.Text.RegularExpressions;

namespace InovaGed.Infrastructure.Observability;

public interface ITelemetrySanitizer
{
    string Sanitize(string key, string? value);
}
public sealed class TelemetrySanitizationOptions
{
    public string RedactedValue { get; set; } = "[REDACTED]";
    public int MaximumValueLength { get; set; } = 512;
}
public sealed partial class TelemetrySanitizer : ITelemetrySanitizer
{
    private static readonly string[] Blocked = ["password", "pwd", "secret", "token", "authorization", "cookie", "connectionstring", "privatekey", "cpf", "patient", "documentcontent", "filecontent"];
    private readonly TelemetrySanitizationOptions options;
    public TelemetrySanitizer(TelemetrySanitizationOptions? options = null) => this.options = options ?? new();
    public string Sanitize(string key, string? value)
    {
        if (Blocked.Any(x => key.Contains(x, StringComparison.OrdinalIgnoreCase))) return options.RedactedValue;
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var safe = Cpf().Replace(value, options.RedactedValue);
        safe = Email().Replace(safe, options.RedactedValue);
        safe = Ip().Replace(safe, "[MASKED_IP]");
        safe = InternalPath().Replace(safe, "[MASKED_PATH]");
        return safe.Length > options.MaximumValueLength ? safe[..options.MaximumValueLength] : safe;
    }
    [GeneratedRegex(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b")] private static partial Regex Cpf();
    [GeneratedRegex(@"\b[^\s@]+@[^\s@]+\.[^\s@]+\b")] private static partial Regex Email();
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")] private static partial Regex Ip();
    [GeneratedRegex(@"(?i)(?:[a-z]:\\|/(?:home|var|srv|opt)/)[^\s]+") ] private static partial Regex InternalPath();
}
