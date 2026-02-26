using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrStatusQueries : IOcrStatusQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OcrStatusQueries> _logger;

    public OcrStatusQueries(IDbConnectionFactory db, ILogger<OcrStatusQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<Guid, OcrJobStatusDto>> GetLatestByVersionIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken ct)
    {
        if (versionIds.Count == 0)
            return new Dictionary<Guid, OcrJobStatusDto>();

        const string sql = @"
SELECT DISTINCT ON (j.document_version_id)
  j.document_version_id AS VersionId,
  j.status::text        AS StatusText,
  j.id                  AS JobId,
  j.requested_at        AS RequestedAt,
  j.started_at          AS StartedAt,
  j.finished_at         AS FinishedAt,
  j.error_message       AS ErrorMessage,
  j.invalidate_digital_signatures AS InvalidateDigitalSignatures
FROM ged.ocr_job j
WHERE j.tenant_id = @tenantId
  AND j.document_version_id = ANY(@versionIds)
ORDER BY j.document_version_id, j.requested_at DESC;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var rows = await conn.QueryAsync(
                new CommandDefinition(sql, new { tenantId, versionIds = versionIds.ToArray() }, cancellationToken: ct));

            var dict = new Dictionary<Guid, OcrJobStatusDto>();

            foreach (var r in rows)
            {
                Guid versionId = r.versionid;
                string statusText = r.statustext;

                if (!Enum.TryParse<OcrStatusEnum>(statusText, ignoreCase: true, out var status))
                    status = OcrStatusEnum.PENDING;

                dict[versionId] = new OcrJobStatusDto(
                    VersionId: versionId,
                    Status: status,
                    JobId: (long)r.jobid,
                    RequestedAt: (DateTime)r.requestedat,
                    StartedAt: (DateTime?)r.startedat,
                    FinishedAt: (DateTime?)r.finishedat,
                    ErrorMessage: (string?)r.errormessage,
                    InvalidateDigitalSignatures: (bool)r.invalidatedigitalsignatures
                );
            }

            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar status OCR. Tenant={TenantId}", tenantId);
            throw;
        }
    }
}