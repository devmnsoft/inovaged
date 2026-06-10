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
        bool forceStopOcr,
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
            // 0) ADMIN pode forçar parada da fila OCR para permitir exclusão.
            if (forceStopOcr)
            {
                await conn.ExecuteAsync(new CommandDefinition(@"
update ged.ocr_job
set status = 'CANCELLED'::ged.ocr_status_enum,
    finished_at = now(),
    lease_expires_at = null,
    error_message = 'Cancelado por exclusão forçada por ADMIN',
    cancel_requested = true,
    cancel_requested_at = now(),
    cancelled_by = @userId,
    cancel_reason = 'Exclusão forçada por ADMIN'
where tenant_id = @tenantId
  and document_version_id in (
      select id from ged.document_version where tenant_id = @tenantId and document_id = @documentId
  )
  and status in ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum);",
                    new { tenantId, documentId, userId },
                    transaction: tx,
                    cancellationToken: ct));
            }

            // 1) valida documento ativo e coleta metadados mínimos. Não apaga/atualiza versões, busca, OCR, preview_status ou arquivos.
            var document = await conn.QuerySingleOrDefaultAsync<DeleteDocumentInfo>(new CommandDefinition(@"
select d.id,
       exists (
           select 1
           from ged.document_version v
           where v.tenant_id = d.tenant_id
             and v.document_id = d.id
             and coalesce(v.is_partial_document, false) = true
       ) or exists (
           select 1
           from ged.document_partial_part pp
           where pp.tenant_id = d.tenant_id
             and pp.document_id = d.id
             and coalesce(pp.reg_status, 'A') = 'A'
       ) as ""WasPartialDocument"",
       (
           select coalesce(v.partial_group_id, pp.partial_group_id)
           from ged.document_version v
           full join ged.document_partial_part pp
             on pp.tenant_id = v.tenant_id
            and pp.version_id = v.id
           where coalesce(v.tenant_id, pp.tenant_id) = d.tenant_id
             and coalesce(v.document_id, pp.document_id) = d.id
             and coalesce(v.partial_group_id, pp.partial_group_id) is not null
           limit 1
       ) as ""PartialGroupId""
from ged.document d
where d.tenant_id = @tenantId
  and d.id = @documentId
  and coalesce(d.reg_status, 'A') = 'A';",
                new { tenantId, documentId },
                transaction: tx,
                cancellationToken: ct));

            if (document is null)
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("DOC_NOT_FOUND", "Documento não encontrado ou já excluído.");
            }

            const string reason = "Exclusão lógica solicitada no GED";
            var correlationId = Guid.NewGuid().ToString("N");

            // 2) exclusão lógica somente em ged.document. Versões, busca, OCR, partes e arquivos físicos permanecem preservados.
            var rows = await conn.ExecuteAsync(new CommandDefinition(@"
update ged.document
set reg_status = 'I',
    deleted_at = now(),
    deleted_by = @userId,
    deleted_reason = @reason,
    updated_at = now(),
    updated_by = @userId
where tenant_id = @tenantId
  and id = @documentId
  and coalesce(reg_status, 'A') = 'A';",
                new { tenantId, documentId, userId, reason },
                tx,
                cancellationToken: ct));

            if (rows == 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("DOC_NOT_FOUND", "Documento não encontrado ou já excluído.");
            }

            await conn.ExecuteAsync(new CommandDefinition(@"
insert into ged.app_audit_log(tenant_id, user_id, created_at, action, entity_name, entity_id, message, details, correlation_id, reg_status)
values(
    @tenantId,
    @userId,
    now(),
    'DOCUMENT_DELETED',
    'document',
    @documentId::text,
    'Documento excluído logicamente',
    jsonb_build_object(
        'tenantId', @tenantId,
        'userId', @userId,
        'documentId', @documentId,
        'reason', @reason,
        'forceStopOcr', @forceStopOcr,
        'wasPartialDocument', @wasPartialDocument,
        'partialGroupId', @partialGroupId,
        'correlationId', @correlationId,
        'timestampUtc', now()
    ),
    @correlationId,
    'A'
)
on conflict do nothing;",
                new { tenantId, userId, documentId, reason, forceStopOcr, document.WasPartialDocument, document.PartialGroupId, correlationId },
                tx,
                cancellationToken: ct));

            if (document.WasPartialDocument)
            {
                await conn.ExecuteAsync(new CommandDefinition(@"
insert into ged.app_audit_log(tenant_id, user_id, created_at, action, entity_name, entity_id, message, details, correlation_id, reg_status)
values(
    @tenantId,
    @userId,
    now(),
    'DOCUMENT_PARTIAL_DOCUMENT_DELETED',
    'document',
    @documentId::text,
    'Documento parcial excluído logicamente; partes preservadas para auditoria',
    jsonb_build_object(
        'tenantId', @tenantId,
        'userId', @userId,
        'documentId', @documentId,
        'reason', @reason,
        'forceStopOcr', @forceStopOcr,
        'wasPartialDocument', true,
        'partialGroupId', @partialGroupId,
        'correlationId', @correlationId,
        'timestampUtc', now()
    ),
    @correlationId,
    'A'
)
on conflict do nothing;",
                    new { tenantId, userId, documentId, reason, forceStopOcr, document.PartialGroupId, correlationId },
                    tx,
                    cancellationToken: ct));
            }

            await tx.CommitAsync(ct);

            // 4) mantém arquivo físico, versões, OCR, busca e partes para aderir à exclusão lógica e à auditoria.
            _logger.LogInformation("Documento inativado logicamente. Tenant={TenantId} DocumentId={DocumentId} WasPartialDocument={WasPartialDocument} PartialGroupId={PartialGroupId}", tenantId, documentId, document.WasPartialDocument, document.PartialGroupId);
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

    private sealed class DeleteDocumentInfo
    {
        public Guid Id { get; set; }
        public bool WasPartialDocument { get; set; }
        public Guid? PartialGroupId { get; set; }
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
