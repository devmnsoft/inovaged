namespace InovaGed.Application.PhysicalArchive;

public sealed record LabelPrintRequest(
    Guid TenantId,
    Guid UserId,
    string SubjectType,
    Guid SubjectId,
    string TemplateCode,
    string SnapshotJson,
    string? IpAddress,
    string? UserAgent,
    string? ReprintReason)
{
    // Keep print metadata out of the positional constructor so existing callers remain source-compatible.
    public string PrintChannel { get; init; } = "WEB";
    public string? PrintMode { get; init; }
    public int? TemplateVersion { get; init; }
    public Guid? LogoAssetId { get; init; }
    public string? LogoBrandName { get; init; }
    public decimal? LogoWidthMm { get; init; }
    public decimal? LogoHeightMm { get; init; }
    public string? LogoFitMode { get; init; }
    public string? LogoPosition { get; init; }
    public Guid? CalibrationProfileId { get; init; }
    public string? TraceCode { get; init; }
}

public interface ILabelPrintRegistrar
{
    Task<InovaGed.Application.Labels.LabelTraceIssued> RegisterAsync(LabelPrintRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Canonical boundary for auditable label printing.</summary>
public interface ILabelPrintService
{
    Task<InovaGed.Application.Labels.LabelTraceIssued> RegisterAsync(LabelPrintRequest request, CancellationToken cancellationToken = default);
}

public interface ILabelPayloadBuilder
{
    string Build(object snapshot);
}

public sealed record LabelTemplate(string Code, string Version, string SubjectType);

public interface ILabelTemplateService
{
    LabelTemplate GetCurrent(string subjectType);
}

public interface ILabelQrCodeService
{
    string CreateTrackingSvg(string authorizedTrackingUrl);
}
