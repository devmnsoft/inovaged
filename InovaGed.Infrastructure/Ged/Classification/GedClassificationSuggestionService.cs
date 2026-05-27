using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Classification;

namespace InovaGed.Infrastructure.Ged.Classification;

public sealed class GedClassificationSuggestionService : IGedClassificationSuggestionService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<GedClassificationSuggestionService> _logger;
    public GedClassificationSuggestionService(IDbConnectionFactory db, ILogger<GedClassificationSuggestionService> logger) { _db = db; _logger = logger; }

    public async Task<ClassificationSuggestionDto?> SuggestForDocumentAsync(Guid tenantId, Guid documentId, Guid userId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var row = await conn.QueryFirstOrDefaultAsync<(string? title, string? ocr, string? folderPath)>(new CommandDefinition(@"select d.title, ds.ocr_text as ocr, f.path as folderPath from ged.document d left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id where d.tenant_id=@tenantId and d.id=@documentId", new { tenantId, documentId }, cancellationToken: ct));
            if (row.title is null) return null;
            var text = $"{row.title} {row.ocr} {row.folderPath}".ToLowerInvariant();
            var reasons = new List<string>();
            decimal confidence = 0;
            string? type = null;
            if (text.Contains("prontuário")) { confidence += 40; reasons.Add("Contém palavra-chave prontuário"); type = "Prontuário Médico"; }
            if (text.Contains("contrato")) { confidence += 40; reasons.Add("Contém palavra-chave contrato"); type ??= "Contrato"; }
            if (text.Contains("nota fiscal")) { confidence += 40; reasons.Add("Contém palavra-chave nota fiscal"); type ??= "Nota Fiscal"; }
            if (text.Contains("financeiro") || text.Contains("pagamento") || text.Contains("empenho")) { confidence += 20; reasons.Add("Contexto financeiro detectado"); }
            if ((row.folderPath ?? string.Empty).ToLowerInvariant().Contains("financeiro")) { confidence += 20; reasons.Add("Pasta compatível com financeiro"); }
            confidence = Math.Min(100, confidence);
            return new ClassificationSuggestionDto { DocumentId = documentId, SuggestedDocumentTypeName = type, Confidence = confidence, Reasons = reasons, Source = "RULES_V1", GeneratedAt = DateTimeOffset.UtcNow, SuggestedSecurityLevel = confidence < 50 ? "REVIEW_REQUIRED" : null };
        }
        catch (Exception ex) { _logger.LogError(ex, "Erro SuggestForDocumentAsync tenant={TenantId} document={DocumentId}", tenantId, documentId); throw; }
    }

    public async Task<IReadOnlyList<ClassificationSuggestionDto>> SuggestBatchAsync(Guid tenantId, IReadOnlyList<Guid> documentIds, Guid userId, CancellationToken ct)
    {
        var limited = documentIds.Distinct().Take(100).ToArray();
        var list = new List<ClassificationSuggestionDto>();
        foreach (var id in limited)
        {
            var s = await SuggestForDocumentAsync(tenantId, id, userId, ct);
            if (s is not null) list.Add(s);
        }
        return list;
    }
}
