using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Retention;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class DocumentBulkClassificationService(IDbConnectionFactory db, IDocumentCommands commands,
    IRetentionRecalcService retention, IAuditWriter audit) : IDocumentBulkClassificationService
{
    public async Task<DocumentBulkClassificationResult> ApplyAsync(Guid tenantId, Guid userId,
        IReadOnlyCollection<Guid> documentIds, Guid classificationId, CancellationToken ct)
    {
        var ids = documentIds.Where(x => x != Guid.Empty).Distinct().Take(500).ToArray();
        await using var connection = await db.OpenAsync(ct);
        var classificationExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(select 1 from ged.document_type c
 where c.tenant_id=@tenantId and c.id=@classificationId and coalesce(c.reg_status,'A')='A');
""", new { tenantId, classificationId }, cancellationToken: ct));
        if (!classificationExists) throw new ArgumentException("Classificação ativa não encontrada.");
        var accessible = (await connection.QueryAsync<Guid>(new CommandDefinition("""
select id from ged.document where tenant_id=@tenantId and id=any(@ids) and coalesce(reg_status,'A')='A';
""", new { tenantId, ids }, cancellationToken: ct))).ToHashSet();
        var items = new List<DocumentBulkClassificationItem>(ids.Length);
        foreach (var id in ids)
        {
            if (!accessible.Contains(id)) { items.Add(new(id, false, "Documento não encontrado ou inacessível.")); continue; }
            try
            {
                // One browser request, using the official command and retention pipeline per document.
                await commands.ApplyClassificationAsync(tenantId, userId, id, classificationId, ct);
                await retention.RunOneAsync(tenantId, id, 30, ct);
                items.Add(new(id, true, "Classificação aplicada."));
            }
            catch { items.Add(new(id, false, "Não foi possível classificar o documento.")); }
        }
        var succeeded = items.Count(x => x.Success);
        await audit.WriteAsync(tenantId, userId, "DOCUMENT_BULK_CLASSIFIED", "DOCUMENT", null,
            "Classificação em massa concluída", null, null,
            new { requested = ids.Length, succeeded, failed = ids.Length - succeeded, classificationId }, ct);
        return new(ids.Length, succeeded, ids.Length - succeeded, items);
    }
}
