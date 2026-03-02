using System.Data;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Documents;

public sealed class DocumentCommands : IDocumentCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<DocumentCommands> _logger;

    public DocumentCommands(
        IDbConnectionFactory db,
        IFileStorage storage,
        ILogger<DocumentCommands> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result> DeleteAsync(
        Guid tenantId,
        Guid documentId,
        Guid? userId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            ">>> DocumentCommands.DeleteAsync START | Tenant={Tenant} Document={Document}",
            tenantId, documentId);

        // ✅ CORREÇÃO CRÍTICA: garante Dispose da conexão
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            // 1) coleta paths
            var paths = (await conn.QueryAsync<string>(
                new CommandDefinition(@"
select v.storage_path
from ged.document_version v
where v.tenant_id = @tenantId
  and v.document_id = @documentId
  and v.storage_path is not null
  and v.storage_path <> '';
",
                new { tenantId, documentId },
                transaction: tx,
                cancellationToken: ct)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 2) apaga versões
            await conn.ExecuteAsync(new CommandDefinition(@"
delete from ged.document_version
where tenant_id = @tenantId
  and document_id = @documentId;",
                new { tenantId, documentId },
                tx,
                cancellationToken: ct));

            // 3) apaga documento
            var rows = await conn.ExecuteAsync(new CommandDefinition(@"
delete from ged.document
where tenant_id = @tenantId
  and id = @documentId;",
                new { tenantId, documentId },
                tx,
                cancellationToken: ct));

            if (rows == 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("DOC_NOT_FOUND", "Documento não encontrado.");
            }

            await tx.CommitAsync(ct);

            // 4) remove arquivos do storage (fora da transação)
            foreach (var path in paths)
                await _storage.DeleteIfExistsAsync(path, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }

            _logger.LogError(ex,
                ">>> DocumentCommands.DeleteAsync ERROR | Tenant={Tenant} Document={Document}",
                tenantId, documentId);

            return Result.Fail("DOC_DELETE_ERROR", "Erro ao excluir documento.");
        }
        finally
        {
            _logger.LogInformation(">>> DocumentCommands.DeleteAsync END");
        }
    }


    public async Task ApplyClassificationAsync(Guid tenantId, Guid userId, Guid documentId, Guid classificationId, CancellationToken ct)
    {
        const string sql = @"
update ged.document
set classification_id = @classificationId,
    classification_version_id = (
      select v.id
      from ged.classification_plan_version v
      where v.tenant_id = @tenantId
      order by v.version_no desc
      limit 1
    ),
    updated_at = now(),
    updated_by = @userId
where tenant_id = @tenantId
  and id = @documentId;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, userId, documentId, classificationId }, cancellationToken: ct));
            if (rows == 0) throw new InvalidOperationException("Documento não encontrado para aplicar classificação.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyClassificationAsync failed. Tenant={TenantId} Doc={DocId} Class={ClassId}",
                tenantId, documentId, classificationId);
            throw;
        }
    }
}
