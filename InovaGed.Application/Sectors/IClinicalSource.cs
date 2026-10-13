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

public sealed record ClinicalDocumentRef(string ExternalId, string? Name, string? Status, DateTimeOffset? UpdatedAt);
public sealed record ClinicalDocumentResult(ClinicalSourceStatus Status, string Message, IReadOnlyList<ClinicalDocumentRef> Documents);
public sealed record ClinicalContentResult(ClinicalSourceStatus Status, string Message, Stream? Content, string? FileName, string? ContentType);

public interface IClinicalSource
{
    Task<ClinicalSourceResult> FindAsync(Guid tenantId, string externalCode, string? sourceSystem, string? sourceUnit, string identifierKind, CancellationToken ct);
    Task<ClinicalDocumentResult> ListDocumentsAsync(Guid tenantId, string? sourceSystem, string? sourceUnit, string patientCode, string encounterCode, CancellationToken ct);
    Task<ClinicalContentResult> OpenContentAsync(Guid tenantId, string externalDocumentId, CancellationToken ct);
}

public sealed class ClinicalSourceOptions
{
    public const string SectionName = "ClinicalSource";
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? PatientPath { get; set; }
    public string? DocumentsPath { get; set; }
    public string? ContentPath { get; set; }
    public string ApiKeyEnvironmentVariable { get; set; } = "INOVAGED_CLINICAL_SOURCE_KEY";
}
