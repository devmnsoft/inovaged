using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentClassificationAuditQueries : IDocumentClassificationAuditQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentClassificationAuditQueries> _logger;

    public DocumentClassificationAuditQueries(
        IDbConnectionFactory db,
        ILogger<DocumentClassificationAuditQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ClassificationAuditRowDto>> ListByDocumentAsync(
        Guid tenantId,
        Guid documentId,
        int take,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
  id                 AS ""Id"",
  tenant_id          AS ""TenantId"",
  document_id        AS ""DocumentId"",
  user_id            AS ""UserId"",
  action             AS ""Action"",
  method             AS ""Method"",
  before_json::text  AS ""BeforeJson"",
  after_json::text   AS ""AfterJson"",
  source             AS ""Source"",
  created_at         AS ""CreatedAt""
FROM ged.document_classification_audit
WHERE tenant_id = @TenantId
  AND document_id = @DocumentId
  AND reg_status = 'A'
ORDER BY created_at DESC
LIMIT @Take;";

        try
        {
            await using var con = await _db.OpenAsync(ct);

            var rows = await con.QueryAsync<ClassificationAuditRowDto>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        Take = take < 1 ? 20 : take
                    },
                    cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao listar auditoria de classificação | Tenant={TenantId} Doc={DocumentId}",
                tenantId,
                documentId);

            throw;
        }
    }
}