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
