using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace InovaGed.Web.Middleware;

public sealed class SuspiciousRequestOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxPathLength { get; set; } = 2048;
    public int MaxLoggedPathLength { get; set; } = 512;
    public int MaxLoggedQueryLength { get; set; } = 256;
    public int MaxLoggedUserAgentLength { get; set; } = 256;
}

public sealed class SuspiciousRequestMiddleware
{
    private static readonly HashSet<string> SensitiveSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", "vendor", "node_modules", "phpunit", "cgi-bin", "actuator", "webshell"
    };

    private static readonly HashSet<string> SensitiveFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", "web.config", "package.json", "composer.json", "shell.aspx", "shell.asp", "telerik.web.ui.dialoghandler.aspx"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<SuspiciousRequestMiddleware> _logger;
    private readonly SuspiciousRequestOptions _options;

    public SuspiciousRequestMiddleware(
        RequestDelegate next,
        ILogger<SuspiciousRequestMiddleware> logger,
        IOptions<SuspiciousRequestOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var rawTarget = BuildRawTarget(context);
        var result = InspectPath(rawTarget, _options);
        if (result.Action == PathInspectionAction.Allow)
        {
            await _next(context);
            return;
        }

        LogSecurityEvent(context, result);

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = result.Action == PathInspectionAction.UriTooLong
            ? StatusCodes.Status414UriTooLong
            : StatusCodes.Status404NotFound;

        await context.Response.WriteAsJsonAsync(new
        {
            status = context.Response.StatusCode,
            title = result.Action == PathInspectionAction.UriTooLong ? "URI too long" : "Not found",
            correlationId = context.TraceIdentifier
        }, context.RequestAborted);
    }

    internal static PathInspectionResult InspectPath(string? rawPath, SuspiciousRequestOptions options)
    {
        var safeOptions = options ?? new SuspiciousRequestOptions();
        if (string.IsNullOrWhiteSpace(rawPath)) return PathInspectionResult.Allow(string.Empty, 0);

        var withoutQuery = rawPath.Split('?', 2)[0].Trim();
        var originalLength = withoutQuery.Length;
        if (originalLength > safeOptions.MaxPathLength)
        {
            return PathInspectionResult.Block(PathInspectionAction.UriTooLong, "PATH_TOO_LONG", withoutQuery, originalLength);
        }

        if (withoutQuery.IndexOf('\0') >= 0)
        {
            return PathInspectionResult.Block(PathInspectionAction.Block, "NULL_BYTE", withoutQuery, originalLength);
        }

        string decoded;
        try
        {
            decoded = WebUtility.UrlDecode(withoutQuery) ?? withoutQuery;
        }
        catch (ArgumentException)
        {
            return PathInspectionResult.Block(PathInspectionAction.Block, "INVALID_ENCODING", withoutQuery, originalLength);
        }

        if (decoded.IndexOf('\0') >= 0)
        {
            return PathInspectionResult.Block(PathInspectionAction.Block, "NULL_BYTE", decoded, originalLength);
        }

        var normalized = CollapseSlashes(decoded.Replace('\\', '/').Trim());
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return PathInspectionResult.Allow(normalized, originalLength);

        var last = segments[^1];
        if (IsSensitiveFile(last)) return PathInspectionResult.Block(PathInspectionAction.Block, "SENSITIVE_FILE", normalized, originalLength);

        foreach (var segment in segments)
        {
            if (SensitiveSegments.Contains(segment))
            {
                return PathInspectionResult.Block(PathInspectionAction.Block, "SENSITIVE_SEGMENT", normalized, originalLength);
            }
        }

        if (SegmentsEqual(segments, ".git", "config")) return PathInspectionResult.Block(PathInspectionAction.Block, "GIT_CONFIG", normalized, originalLength);
        if (SegmentsEqual(segments, ".svn", "entries")) return PathInspectionResult.Block(PathInspectionAction.Block, "SVN_ENTRIES", normalized, originalLength);
        if (SegmentsEqual(segments, "containers", "json")) return PathInspectionResult.Block(PathInspectionAction.Block, "DOCKER_CONTAINERS", normalized, originalLength);
        if (SegmentsEqual(segments, "v2", "_catalog")) return PathInspectionResult.Block(PathInspectionAction.Block, "REGISTRY_CATALOG", normalized, originalLength);

        return PathInspectionResult.Allow(normalized, originalLength);
    }

    private static bool IsSensitiveFile(string fileName)
    {
        if (SensitiveFiles.Contains(fileName)) return true;
        if (fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)) return true;
        return fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SegmentsEqual(string[] segments, string first, string second) =>
        segments.Length >= 2
        && segments[^2].Equals(first, StringComparison.OrdinalIgnoreCase)
        && segments[^1].Equals(second, StringComparison.OrdinalIgnoreCase);

    private static string CollapseSlashes(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousSlash = false;
        foreach (var c in value)
        {
            if (c == '/')
            {
                if (!previousSlash) builder.Append(c);
                previousSlash = true;
                continue;
            }
            previousSlash = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static string BuildRawTarget(HttpContext context) =>
        string.Concat(context.Request.Path.Value, context.Request.QueryString.Value);

    private void LogSecurityEvent(HttpContext context, PathInspectionResult result)
    {
        var pathSummary = Truncate(result.NormalizedPath, _options.MaxLoggedPathLength);
        var query = Truncate(context.Request.QueryString.Value ?? string.Empty, _options.MaxLoggedQueryLength);
        var userAgent = Truncate(context.Request.Headers.UserAgent.ToString(), _options.MaxLoggedUserAgentLength);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.NormalizedPath)));

        _logger.LogWarning(
            "SECURITY_SUSPICIOUS_REQUEST Ip={Ip} Method={Method} PathSummary={PathSummary} PathHash={PathHash} PathLength={PathLength} QuerySummary={QuerySummary} UserAgentSummary={UserAgentSummary} Category={Category} Action={Action} TimestampUtc={TimestampUtc:o} CorrelationId={CorrelationId}",
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Method,
            pathSummary,
            hash,
            result.OriginalLength,
            query,
            userAgent,
            result.Category,
            result.Action.ToString(),
            DateTimeOffset.UtcNow,
            context.TraceIdentifier);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}

internal enum PathInspectionAction
{
    Allow,
    Block,
    UriTooLong
}

internal sealed record PathInspectionResult(
    PathInspectionAction Action,
    string Category,
    string NormalizedPath,
    int OriginalLength)
{
    public static PathInspectionResult Allow(string normalizedPath, int originalLength) =>
        new(PathInspectionAction.Allow, "ALLOWED", normalizedPath, originalLength);

    public static PathInspectionResult Block(PathInspectionAction action, string category, string normalizedPath, int originalLength) =>
        new(action, category, normalizedPath, originalLength);
}
