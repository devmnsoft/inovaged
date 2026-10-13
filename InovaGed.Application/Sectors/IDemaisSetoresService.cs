namespace InovaGed.Application.Sectors;

public interface IDemaisSetoresService
{
    Task<bool> CanEnterAsync(Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct);
    Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, bool isFullAdmin, string permission, CancellationToken ct);
    Task EnsureTenantAsync(Guid tenantId, CancellationToken ct);
    Task<ClinicalSourceResult> FindPatientAsync(Guid tenantId, string? externalCode, string? sourceSystem, string? sourceUnit, CancellationToken ct);
    Task<IReadOnlyList<string>> VisibleCatalogCodesAsync(Guid tenantId, Guid userId, bool isFullAdmin, CancellationToken ct);
    Task<SectorRegistrationResult> RegisterAsync(SectorRegistrationRequest request, CancellationToken ct);
    Task<SectorRegistrationResult> LinkExistingAsync(SectorLinkExistingRequest request, CancellationToken ct);
    Task<SectorRegistrationResult> RetryProcessingAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid linkId, string ip, string userAgent, CancellationToken ct);
    Task<string?> ReviewAsync(SectorReviewRequest request, CancellationToken ct);
    Task<string?> CorrectLinkAsync(SectorCorrectionRequest request, CancellationToken ct);
    Task<IReadOnlyList<SectorSearchHit>> SearchAsync(SectorSearchRequest request, CancellationToken ct);
    Task RefreshExtractionAsync(Guid tenantId, Guid encounterId, CancellationToken ct);
    Task<string?> ReviewFieldAsync(SectorFieldReviewRequest request, CancellationToken ct);
    Task<string?> RecordPhysicalEvidenceAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid linkId, string evidence, string ip, string userAgent, CancellationToken ct);
    Task<string?> ReviewFindingAsync(Guid tenantId, Guid userId, bool isFullAdmin, Guid findingId, string status, string? justification, string ip, string userAgent, CancellationToken ct);
    Task<SectorDeliveryResult> PrepareDeliveryAsync(SectorDeliveryRequest request, CancellationToken ct);
    Task<SectorEmbedResult> CreateEmbedAsync(SectorEmbedRequest request, CancellationToken ct);
    Task<bool> GrantIsActiveAsync(string token, Guid versionId, CancellationToken ct);
    Task RevokeEmbedAsync(Guid tenantId, Guid grantId, Guid actorId, CancellationToken ct);
}

public sealed record SectorRegistrationRequest(
    Guid TenantId, Guid UserId, bool IsFullAdmin, string Ip, string UserAgent,
    Guid PatientId, Guid EncounterId, string CatalogCode, string? SectorCode, DateOnly? DocumentDate,
    string? Notes, string? IssuerName, string? InformationOwner, string OriginalFileName, string ContentType,
    byte[] Content, bool ConfirmAnotherOccurrence);

public sealed record SectorLinkExistingRequest(
    Guid TenantId, Guid UserId, bool IsFullAdmin, string Ip, string UserAgent,
    Guid PatientId, Guid EncounterId, Guid DocumentId, string CatalogCode, bool ConfirmAnotherOccurrence);

public sealed record SectorRegistrationResult(bool Stored, string Stage, string Message, Guid? LinkId, Guid? DocumentId, Guid? VersionId);

public sealed record SectorReviewRequest(Guid TenantId, Guid UserId, bool IsFullAdmin, Guid LinkId, string ToStatus, string? Justification, string Ip, string UserAgent);
public sealed record SectorCorrectionRequest(Guid TenantId, Guid UserId, bool IsFullAdmin, Guid LinkId, Guid ToEncounterId, string Reason, string Ip, string UserAgent);
public sealed record SectorSearchRequest(Guid TenantId, Guid UserId, bool IsFullAdmin, Guid EncounterId, string Term, bool IncludeOtherEncounters);
public sealed record SectorSearchHit(Guid DocumentId, Guid VersionId, string Name, string Excerpt, int? Page, string EncounterCode, string TextOrigin);
public sealed record SectorFieldReviewRequest(Guid TenantId, Guid UserId, bool IsFullAdmin, Guid FieldId, string Status, string? Value, string Ip, string UserAgent);
public sealed record SectorDeliveryRequest(Guid TenantId, Guid UserId, bool IsFullAdmin, Guid EncounterId, string ProfileCode, string? Recipient, IReadOnlyList<Guid> LinkIds, string Ip, string UserAgent);
public sealed record SectorDeliveryResult(bool Recorded, string Status, string Message, IReadOnlyList<string> Blockers);
public sealed record SectorEmbedRequest(Guid TenantId, Guid UserId, bool IsFullAdmin, Guid VersionId, string? Origin, string Ip, string UserAgent);
public sealed record SectorEmbedResult(bool Created, string Message, string? Path);
