using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentTypeQueries : IDocumentTypeQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentTypeQueries> _logger;

    public DocumentTypeQueries(IDbConnectionFactory db, ILogger<DocumentTypeQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Guid?> GetIdByCodeAsync(Guid tenantId, string code, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("tenantId inválido.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(code)) return null;

        const string sql = @"
SELECT id
FROM ged.document_type
WHERE tenant_id = @TenantId
  AND upper(code) = upper(@Code)
LIMIT 1;
";

        try
        {
            await using var con = await _db.OpenAsync(ct);
            return await con.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(sql, new { TenantId = tenantId, Code = code.Trim() }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em GetIdByCodeAsync | Tenant={TenantId} Code={Code}", tenantId, code);
            throw;
        }
    }
}