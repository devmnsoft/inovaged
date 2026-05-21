using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Audit;

namespace InovaGed.Infrastructure.Classification;

public sealed class GedIntelligenceService : IGedIntelligenceService
{
    private readonly IDbConnectionFactory _db;
    private readonly HybridDocumentTypeSuggester _suggester;
    private readonly IAuditWriter _auditWriter;

    public GedIntelligenceService(IDbConnectionFactory db, HybridDocumentTypeSuggester suggester, IAuditWriter auditWriter)
    {
        _db = db;
        _suggester = suggester;
        _auditWriter = auditWriter;
    }

    public async Task<IReadOnlyList<Guid>> DetectDuplicatesAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var ids = await conn.QueryAsync<Guid>(new CommandDefinition(@"
select d.id
from ged.documents d
where d.tenant_id=@tenantId
group by d.hash_sha256, d.id
having count(*) over(partition by d.hash_sha256) > 1
limit 100", new { tenantId }, cancellationToken: ct));
        return ids.AsList();
    }

    public async Task<IReadOnlyList<DocumentTypeSuggestionDto>> SuggestTagsAsync(Guid tenantId, string text, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var documentTypes = (await conn.QueryAsync<(Guid id, string name)>(new CommandDefinition(@"
select id, name
from ged.document_types
where tenant_id = @tenantId
  and reg_status = 'A'
order by name", new { tenantId }, cancellationToken: ct))).AsList();

        var suggestions = await _suggester.SuggestAsync(
            ocrText: text,
            fileName: null,
            folderName: null,
            title: null,
            types: documentTypes,
            ct: ct);

        return suggestions;
    }

    public async Task<IReadOnlyList<DocumentTypeSuggestionDto>> SuggestTagsAsync(Guid tenantId, Guid documentId, Guid? userId, string text, string? fileName, string? folderName, string? title, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var documentTypes = (await conn.QueryAsync<(Guid id, string name)>(new CommandDefinition(@"
select id, name
from ged.document_types
where tenant_id = @tenantId
  and reg_status = 'A'
order by name", new { tenantId }, cancellationToken: ct))).AsList();

        var suggestions = await _suggester.SuggestAsync(
            ocrText: text,
            fileName: fileName,
            folderName: folderName,
            title: title,
            types: documentTypes,
            ct: ct);

        await _auditWriter.WriteAsync(
            tenantId,
            userId,
            action: "UPDATE",
            entityName: "DOCUMENT_CLASSIFICATION_SUGGESTION",
            entityId: documentId,
            summary: $"{suggestions.Count} sugestão(ões) geradas automaticamente.",
            ipAddress: null,
            userAgent: null,
            data: new { documentId, suggestions },
            ct);

        return suggestions;
    }
}
