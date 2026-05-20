using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Classification;

public sealed class GedIntelligenceService : IGedIntelligenceService
{
    private readonly IDbConnectionFactory _db;
    private readonly HybridDocumentTypeSuggester _suggester;

    public GedIntelligenceService(IDbConnectionFactory db, HybridDocumentTypeSuggester suggester)
    {
        _db = db;
        _suggester = suggester;
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
        var suggestions = await _suggester.SuggestAsync(tenantId, text, ct);
        return suggestions.Select(s => new DocumentTypeSuggestionDto(s.TypeId, s.TypeName, s.Confidence, s.Summary)).ToList();
    }
}
