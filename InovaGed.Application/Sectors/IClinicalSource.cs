namespace InovaGed.Application.Sectors;

public enum ClinicalSourceStatus
{
    Found,
    NotFound,
    Unavailable,
    Failed
}

public sealed record ExternalPatient(string SourceSystem, string? SourceUnit, string ExternalCode, string DisplayName);
public sealed record ExternalEncounter(string ExternalCode, string? RawKind, DateTimeOffset? AdmittedAt, DateTimeOffset? DischargedAt, string? Status);

public sealed record ClinicalSourceResult(
    ClinicalSourceStatus Status,
    string Message,
    ExternalPatient? Patient,
    IReadOnlyList<ExternalEncounter> Encounters);

public interface IClinicalSource
{
    Task<ClinicalSourceResult> FindAsync(Guid tenantId, string externalCode, string? sourceSystem, string? sourceUnit, CancellationToken ct);
}
