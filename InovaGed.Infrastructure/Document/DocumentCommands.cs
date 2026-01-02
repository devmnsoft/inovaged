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

    public DocumentCommands(IDbConnectionFactory db, IFileStorage storage, ILogger<DocumentCommands> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid documentId, Guid? userId, CancellationToken ct)
    {
        _logger.LogInformation(
            ">>> DocumentCommands.DeleteAsync START | Tenant={Tenant} Document={Document}",
            tenantId, documentId);

        var conn = await _db.OpenAsync(ct);
        using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            // 1) coleta paths (duas tabelas possíveis)
            var paths = (await conn.QueryAsync<string>(new CommandDefinition(@"
select v.storage_path
from ged.document_versions v
where v.tenant_id = @tenantId and v.document_id = @documentId
  and v.storage_path is not null and v.storage_path <> ''
union all
select v.storage_path
from ged.document_version v
where v.tenant_id = @tenantId and v.document_id = @documentId
  and v.storage_path is not null and v.storage_path <> '';
",
                new { tenantId, documentId },
                transaction: tx,
                cancellationToken: ct))).ToList();

            // 2) apaga versões (duas tabelas possíveis)
            var dv1 = await conn.ExecuteAsync(new CommandDefinition(@"
delete from ged.document_versions
where tenant_id = @tenantId and document_id = @documentId;",
                new { tenantId, documentId }, tx, cancellationToken: ct));

            var dv2 = await conn.ExecuteAsync(new CommandDefinition(@"
delete from ged.document_version
where tenant_id = @tenantId and document_id = @documentId;",
                new { tenantId, documentId }, tx, cancellationToken: ct));

            // 3) apaga documento (duas tabelas possíveis)
            var d1 = await conn.ExecuteAsync(new CommandDefinition(@"
delete from ged.documents
where tenant_id = @tenantId and id = @documentId;",
                new { tenantId, documentId }, tx, cancellationToken: ct));

            var d2 = await conn.ExecuteAsync(new CommandDefinition(@"
delete from ged.document
where tenant_id = @tenantId and id = @documentId;",
                new { tenantId, documentId }, tx, cancellationToken: ct));

            _logger.LogInformation(
                "Delete affected rows | doc_plural={DocPlural} doc_singular={DocSingular} ver_plural={VerPlural} ver_singular={VerSingular}",
                d1, d2, dv1, dv2);

            if (d1 == 0 && d2 == 0)
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                return Result.Fail("DOC_NOT_FOUND", "Documento não encontrado.");
            }

            tx.Commit();

            // 4) apaga arquivos no storage
            foreach (var p in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                await _storage.DeleteIfExistsAsync(p, ct); // silencioso :contentReference[oaicite:1]{index=1}

            return Result.Ok();
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { /* ignore */ }
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

}
