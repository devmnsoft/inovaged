namespace InovaGed.Application.Labels;

public static class LabelTraceStatus
{
    public const string Active = "ACTIVE";
    public const string Replaced = "REPLACED";
    public const string Revoked = "REVOKED";
    public const string Damaged = "DAMAGED";
    public const string Lost = "LOST";
}

public static class LabelScanResult
{
    public const string Valid = "VALID";
    public const string Replaced = "REPLACED";
    public const string Revoked = "REVOKED";
    public const string Unknown = "UNKNOWN";
    public const string TenantMismatch = "TENANT_MISMATCH";
}

public sealed record LabelTraceToken(string Token, string Hash);

/// <summary>Creates opaque, URL-safe tokens and one-way hashes. Raw tokens must never be persisted.</summary>
public interface ILabelTraceTokenService
{
    LabelTraceToken Generate();
    string Hash(string token);
    bool IsValid(string token);
}

public sealed class LabelTracePublicInfo
{
    public LabelTracePublicInfo() { }
    public LabelTracePublicInfo(Guid Id, string TraceCode, string SubjectType, string TemplateCode, int? TemplateVersion, string Status, DateTime IssuedAt, Guid TenantId)
    {
        this.Id = Id;
        this.TraceCode = TraceCode;
        this.SubjectType = SubjectType;
        this.TemplateCode = TemplateCode;
        this.TemplateVersion = TemplateVersion;
        this.Status = Status;
        this.IssuedAt = IssuedAt;
        this.TenantId = TenantId;
    }
    public Guid Id { get; set; }
    public string TraceCode { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public string TemplateCode { get; set; } = "";
    public int? TemplateVersion { get; set; }
    public string Status { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public Guid TenantId { get; set; }
}

public sealed record LabelTraceIssueCommand(Guid TenantId, Guid? LabelPrintId, string SubjectType, Guid? SubjectId,
    string TemplateCode, int? TemplateVersion, Guid? IssuedBy, string? IssuedByName, string? PayloadHash);

public sealed record LabelTraceIssued(LabelTracePublicInfo Trace, string Token, string ShortUrl, Guid? LabelPrintId = null);

public interface ILabelTraceabilityService
{
    Task<LabelTraceIssued> IssueAsync(LabelTraceIssueCommand command, CancellationToken ct);
    Task<LabelTracePublicInfo?> ResolvePublicAsync(string token, CancellationToken ct);
    Task<LabelTracePublicInfo?> ResolveInternalAsync(Guid tenantId, string tokenUrlOrCode, CancellationToken ct);
    Task RegisterScanAsync(LabelTracePublicInfo trace, Guid? userId, string? userName, string source,
        string result, string? ip, string? userAgent, string? location, string? notes, CancellationToken ct);
    Task<Guid> ReplaceAsync(Guid tenantId, string oldTokenUrlOrCode, string reason, string newTemplateCode,
        Guid userId, string? userName, CancellationToken ct);
}
