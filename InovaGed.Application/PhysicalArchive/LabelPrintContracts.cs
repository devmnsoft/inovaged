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
    string? ReprintReason);

public interface ILabelPrintRegistrar
{
    Task RegisterAsync(LabelPrintRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Canonical boundary for auditable label printing.</summary>
public interface ILabelPrintService
{
    Task RegisterAsync(LabelPrintRequest request, CancellationToken cancellationToken = default);
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
