using InovaGed.Domain.Ged;

namespace InovaGed.Application.Ocr;

public sealed record OcrJobStatusDto(
    Guid VersionId,
    OcrStatusEnum Status,
    long JobId,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    string? ErrorMessage,
    bool InvalidateDigitalSignatures);

public interface IOcrStatusQueries
{
    /// <summary>
    /// Retorna o ÚLTIMO job (mais recente) por versão.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, OcrJobStatusDto>> GetLatestByVersionIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken ct);
}
