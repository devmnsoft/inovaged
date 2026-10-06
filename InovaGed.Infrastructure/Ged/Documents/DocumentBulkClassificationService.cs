using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Retention;
using InovaGed.Application.Security;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class DocumentBulkClassificationService(
    IDbConnectionFactory db,
    IDocumentCommands commands,
    IRetentionRecalcService retention,
    IAuditWriter audit,
    IAbacAuthorizationService authorization) : IDocumentBulkClassificationService
{
    public async Task<DocumentBulkClassificationResult> ApplyAsync(Guid tenantId, Guid userId,
        IReadOnlyCollection<Guid> documentIds, Guid classificationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (documentIds.Count > 500)
            throw new ArgumentException("O lote deve conter no máximo 500 documentos.", nameof(documentIds));
        if (documentIds.Count == 0)
            return new(0, 0, 0, Array.Empty<DocumentBulkClassificationItem>());

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

        var validIds = documentIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        var accessibleDbSet = validIds.Length > 0
            ? (await connection.QueryAsync<Guid>(new CommandDefinition("""
select id from ged.document where tenant_id=@tenantId and id=any(@validIds) and coalesce(reg_status,'A')='A';
""", new { tenantId, validIds }, cancellationToken: ct))).ToHashSet()
            : new HashSet<Guid>();

        var authorizedSet = accessibleDbSet.Count > 0
            ? await authorization.FilterDocumentsAsync(tenantId, userId, accessibleDbSet, "EDIT", ct)
            : new HashSet<Guid>();

        var items = new List<DocumentBulkClassificationItem>(documentIds.Count);
        foreach (var id in documentIds)
        {
            ct.ThrowIfCancellationRequested();

            if (id == Guid.Empty)
            {
                items.Add(new(id, false, "ID inválido.", "INVALID_ID"));
                continue;
            }

            if (!authorizedSet.Contains(id))
            {
                items.Add(new(id, false, "Documento não encontrado ou inacessível.", "NOT_FOUND"));
                continue;
            }

            try
            {
                await commands.ApplyClassificationAsync(tenantId, userId, id, classificationId, ct);
                try
                {
                    await retention.RunOneAsync(tenantId, id, 30, ct);
                    items.Add(new(id, true, "Classificação aplicada.", "APPLIED"));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var errorMsg = ex.Message;
                    if (errorMsg.Length > 200) errorMsg = errorMsg[..200];
                    await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_retention_recalc_pending (id, tenant_id, document_id, reason, last_error, next_attempt_at, attempts)
values (gen_random_uuid(), @tenantId, @id, 'BULK_CLASSIFICATION_RECALC', @errorMsg, now(), 0)
on conflict do nothing;
""", new { tenantId, id, errorMsg }, cancellationToken: ct));

                    items.Add(new(id, true, "Classificação aplicada com pendência de temporalidade.", "RETENTION_PENDING"));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                items.Add(new(id, false, "Não foi possível classificar o documento.", "FAILED"));
            }
        }

        var succeeded = items.Count(x => x.Success);
        await audit.WriteAsync(tenantId, userId, "DOCUMENT_BULK_CLASSIFIED", "DOCUMENT", null,
            "Classificação em massa concluída", null, null,
            new { requested = documentIds.Count, succeeded, failed = documentIds.Count - succeeded, classificationId }, ct);
        return new(documentIds.Count, succeeded, documentIds.Count - succeeded, items);
    }
}
