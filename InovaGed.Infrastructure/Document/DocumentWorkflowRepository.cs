using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents.Workflow;
using InovaGed.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Documents;

public sealed class DocumentWorkflowRepository : IDocumentWorkflowRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentWorkflowRepository> _logger;

    public DocumentWorkflowRepository(IDbConnectionFactory db, ILogger<DocumentWorkflowRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(DocumentStatus Status, bool Exists)> GetStatusAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
SELECT d.status::text
FROM ged.document d
WHERE d.tenant_id = @tenantId
  AND d.id = @documentId
LIMIT 1;";

        await using var conn = await _db.OpenAsync(ct);

        var statusText = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            sql, new { tenantId, documentId }, cancellationToken: ct));

        if (statusText is null) return (default, false);

        var status = MapStatus(statusText);
        return (status, true);
    }

    public async Task UpdateStatusAsync(Guid tenantId, Guid documentId, DocumentStatus toStatus, Guid? userId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.document
SET status = @status::ged_document_status,
    updated_at = now(),
    updated_by = @userId
WHERE tenant_id = @tenantId
  AND id = @documentId;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                tenantId,
                documentId,
                status = ToDbStatus(toStatus),
                userId
            },
            cancellationToken: ct));

        if (rows == 0)
            throw new InvalidOperationException("Documento não encontrado ou não pertence ao tenant.");
    }

    public async Task InsertLogAsync(Guid tenantId, Guid documentId, DocumentStatus? fromStatus, DocumentStatus toStatus,
        string? reason, Guid? userId, string? ipAddress, string? userAgent, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO ged.document_workflow_log
(id, tenant_id, document_id, from_status, to_status, reason, created_at, created_by, ip_address, user_agent)
VALUES
(@id, @tenantId, @documentId,
  CASE WHEN @fromStatus IS NULL THEN NULL ELSE @fromStatus::ged_document_status END,
  @toStatus::ged_document_status,
  @reason, now(), @userId, @ipAddress, @userAgent
);";

        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                id = Guid.NewGuid(),
                tenantId,
                documentId,
                fromStatus = fromStatus is null ? null : ToDbStatus(fromStatus.Value),
                toStatus = ToDbStatus(toStatus),
                reason,
                userId,
                ipAddress,
                userAgent
            },
            cancellationToken: ct));
    }

    private static string ToDbStatus(DocumentStatus s) => s switch
    {
        DocumentStatus.Draft => "DRAFT",
        DocumentStatus.InReview => "IN_REVIEW",
        DocumentStatus.InSignature => "IN_SIGNATURE",
        DocumentStatus.Published => "PUBLISHED",
        DocumentStatus.Archived => "ARCHIVED",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null)
    };

    private static DocumentStatus MapStatus(string db) => db switch
    {
        "DRAFT" => DocumentStatus.Draft,
        "IN_REVIEW" => DocumentStatus.InReview,
        "IN_SIGNATURE" => DocumentStatus.InSignature,
        "PUBLISHED" => DocumentStatus.Published,
        "ARCHIVED" => DocumentStatus.Archived,
        _ => DocumentStatus.Draft // fallback defensivo
    };
}
