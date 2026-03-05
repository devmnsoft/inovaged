using System;

namespace InovaGed.Application.Common.Security;

public sealed class AccessFailureLogEntry
{
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    public string? TenantId { get; init; }

    public string? UserId { get; init; }
    public string? UserName { get; init; }

    public string Path { get; init; } = "";
    public string Method { get; init; } = "";
    public string QueryString { get; init; } = "";

    public string? Ip { get; init; }
    public string? UserAgent { get; init; }

    /// <summary>CHALLENGE (401) ou FORBIDDEN (403)</summary>
    public string Reason { get; init; } = "";

    public int StatusCode { get; init; }

    public string? Notes { get; init; }
}