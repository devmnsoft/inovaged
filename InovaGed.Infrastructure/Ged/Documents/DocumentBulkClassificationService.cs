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
        var ids = documentIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return new(0, 0, 0, []);
        if (ids.Length > 500) throw new ArgumentException("O lote deve conter no máximo 500 documentos.", nameof(documentIds));

        await using var connection = await db.OpenAsync(ct);
        var classificationExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
  select 1
  from ged.classification_plan_version_item i
  join ged.classification_plan_version v on v.tenant_id=i.tenant_id and v.id=i.version_id
  where i.tenant_id=@tenantId
    and i.classification_id=@classificationId
    and coalesce(i.is_active,true)
    and v.version_no=(select max(version_no) from ged.classification_plan_version where tenant_id=@tenantId)
);
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
