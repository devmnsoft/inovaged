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
    string? ReprintReason,
    string PrintChannel = "WEB",
    string? PrintMode = null,
    int? TemplateVersion = null,
    Guid? LogoAssetId = null,
    string? LogoBrandName = null,
    decimal? LogoWidthMm = null,
    decimal? LogoHeightMm = null,
    string? LogoFitMode = null,
    string? LogoPosition = null,
    Guid? CalibrationProfileId = null,
    string? TraceCode = null);

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
