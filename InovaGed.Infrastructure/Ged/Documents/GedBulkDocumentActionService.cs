using System.Data;
using System.Security.Claims;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Preview;
using InovaGed.Application.Security;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class GedBulkDocumentActionService : IGedBulkDocumentActionService
{
    private const int MaxDocumentsPerOperation = 500;
    private readonly IDbConnectionFactory _db;
    private readonly IGedAccessPolicyService _accessPolicy;
    private readonly IGedProcessingJobRepository _processingJobs;
    private readonly IAuditWriter _audit;
    private readonly ILogger<GedBulkDocumentActionService> _logger;

    public GedBulkDocumentActionService(IDbConnectionFactory db, IGedAccessPolicyService accessPolicy, IGedProcessingJobRepository processingJobs, IAuditWriter audit, ILogger<GedBulkDocumentActionService> logger)
    {
        _db = db;
        _accessPolicy = accessPolicy;
        _processingJobs = processingJobs;
        _audit = audit;
        _logger = logger;
    }

    public Task<BulkDocumentActionResponse> DeleteAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, BulkDocumentActionRequest request, CancellationToken ct)
        => ExecuteAsync(tenantId, userId, principal, request, "GED_BULK_DELETE", "GED_BULK_DELETE_ITEM", DeleteOneAsync, ct);

    public async Task<BulkDocumentActionResponse> MarkIncompleteAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, BulkDocumentActionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Failure(request, "Informe o motivo para marcar documentos como incompletos.");
        return await ExecuteAsync(tenantId, userId, principal, request, "GED_BULK_MARK_INCOMPLETE", "GED_BULK_MARK_INCOMPLETE_ITEM", MarkIncompleteOneAsync, ct);
    }

    public Task<BulkDocumentActionResponse> MarkCompleteAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, BulkDocumentActionRequest request, CancellationToken ct)
        => ExecuteAsync(tenantId, userId, principal, request, "GED_BULK_MARK_COMPLETE", "GED_BULK_MARK_COMPLETE_ITEM", MarkCompleteOneAsync, ct);

    private async Task<BulkDocumentActionResponse> ExecuteAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, BulkDocumentActionRequest request, string bulkAction, string itemAction, Func<IDbConnection, IDbTransaction, Guid, Guid, Guid, ClaimsPrincipal, BulkDocumentActionRequest, DocumentInfo, CancellationToken, Task<int>> operation, CancellationToken ct)
    {
        var ids = NormalizeIds(request.DocumentIds);
        if (ids.Count == 0) return Failure(request, "Selecione ao menos um documento.");
        if (ids.Count > MaxDocumentsPerOperation) return Failure(request, $"Limite de {MaxDocumentsPerOperation} documentos por operação excedido.");

        var response = new BulkDocumentActionResponse { Requested = ids.Count };
        await _audit.WriteAsync(tenantId, userId, bulkAction + "_START", "GED_DOCUMENT", null, "Ação em massa iniciada", null, null, new { ids.Count, request.Reason }, ct);
        foreach (var documentId in ids)
        {
            try
            {
                await using var conn = await _db.OpenAsync(ct);
                await using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                var doc = await LoadDocumentAsync(conn, tx, tenantId, documentId, ct);
                if (doc is null)
                {
                    await tx.RollbackAsync(ct);
                    await AddFailureAsync(response, tenantId, userId, itemAction + "_FAILED", documentId, null, "Documento não encontrado ou inativo.", ct);
                    continue;
                }

                if (!await CanMutateAsync(tenantId, userId, principal, doc, ct))
                {
                    await tx.RollbackAsync(ct);
                    await AddFailureAsync(response, tenantId, userId, itemAction + "_FAILED", documentId, doc.Title, "Sem permissão para alterar este documento.", ct);
                    continue;
                }

                var rows = await operation(conn, tx, tenantId, userId, documentId, principal, request, doc, ct);
                if (rows <= 0)
                {
                    await tx.RollbackAsync(ct);
                    await AddFailureAsync(response, tenantId, userId, itemAction + "_FAILED", documentId, doc.Title, "Nenhuma linha alterada.", ct);
                    continue;
                }

                await tx.CommitAsync(ct);
                response.Items.Add(new BulkDocumentActionItemResult { DocumentId = documentId, Title = doc.Title, Success = true, Message = "Concluído." });
                await _audit.WriteAsync(tenantId, userId, itemAction + "_SUCCESS", "GED_DOCUMENT", documentId, "Item concluído em ação em massa", null, null, new { doc.Title, request.Reason }, ct);
                var documentAction = bulkAction switch
                {
                    "GED_BULK_DELETE" => "DOCUMENT_DELETED",
                    "GED_BULK_MARK_INCOMPLETE" => "DOCUMENT_MARKED_INCOMPLETE",
                    "GED_BULK_MARK_COMPLETE" => "DOCUMENT_INCOMPLETE_CLEARED",
                    _ => null
                };
                if (documentAction is not null)
                {
                    await _audit.WriteAsync(tenantId, userId, documentAction, "GED_DOCUMENT", documentId, "Documento alterado por ação em massa", null, null, new { doc.Title, request.Reason }, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha em ação em massa GED. Action={Action} Tenant={TenantId} User={UserId} Document={DocumentId}", bulkAction, tenantId, userId, documentId);
                await AddFailureAsync(response, tenantId, userId, itemAction + "_FAILED", documentId, null, "Falha técnica ao processar documento.", CancellationToken.None);
            }
        }
        response.Succeeded = response.Items.Count(x => x.Success);
        response.Failed = response.Items.Count(x => !x.Success);
        response.Success = response.Succeeded > 0 && response.Failed == 0;
        response.Message = response.Failed == 0 ? $"{response.Succeeded} documento(s) processado(s)." : $"{response.Succeeded} concluído(s), {response.Failed} falharam.";
        await _audit.WriteAsync(tenantId, userId, bulkAction + "_FINISH", "GED_DOCUMENT", null, "Ação em massa finalizada", null, null, response, ct);
        return response;
    }

    private async Task<int> DeleteOneAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid userId, Guid documentId, ClaimsPrincipal principal, BulkDocumentActionRequest request, DocumentInfo doc, CancellationToken ct)
        => await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document
SET reg_status='I', deleted_at=now(), deleted_by=@userId, deleted_reason=@reason, updated_at=now(), updated_by=@userId
WHERE tenant_id=@tenantId AND id=@documentId AND coalesce(reg_status,'A')='A';
""", new { tenantId, userId, documentId, reason = string.IsNullOrWhiteSpace(request.Reason) ? "Exclusão lógica em massa no GED" : request.Reason }, tx, cancellationToken: ct));

    private async Task<int> MarkIncompleteOneAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid userId, Guid documentId, ClaimsPrincipal principal, BulkDocumentActionRequest request, DocumentInfo doc, CancellationToken ct)
    {
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document
SET is_document_incomplete=true, incomplete_reason=@reason, incomplete_source='USER_MARKED', updated_at=now(), updated_by=@userId
WHERE tenant_id=@tenantId AND id=@documentId AND coalesce(reg_status,'A')='A';
UPDATE ged.document_version
SET is_document_incomplete=true, incomplete_reason=@reason, incomplete_source='USER_MARKED'
WHERE tenant_id=@tenantId AND id=@versionId;
""", new { tenantId, userId, documentId, versionId = doc.CurrentVersionId, reason = request.Reason }, tx, cancellationToken: ct));
        await TryEnqueueIndexAsync(tenantId, userId, documentId, doc.CurrentVersionId, ct);
        return rows;
    }

    private async Task<int> MarkCompleteOneAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid userId, Guid documentId, ClaimsPrincipal principal, BulkDocumentActionRequest request, DocumentInfo doc, CancellationToken ct)
    {
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document
SET is_document_incomplete=false, incomplete_reason=NULL, incomplete_source=NULL, updated_at=now(), updated_by=@userId
WHERE tenant_id=@tenantId AND id=@documentId AND coalesce(reg_status,'A')='A';
UPDATE ged.document_version
SET is_document_incomplete=false, incomplete_reason=NULL, incomplete_source=NULL
WHERE tenant_id=@tenantId AND id=@versionId;
""", new { tenantId, userId, documentId, versionId = doc.CurrentVersionId }, tx, cancellationToken: ct));
        await TryEnqueueIndexAsync(tenantId, userId, documentId, doc.CurrentVersionId, ct);
        return rows;
    }

    private async Task<DocumentInfo?> LoadDocumentAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, Guid documentId, CancellationToken ct)
        => await conn.QuerySingleOrDefaultAsync<DocumentInfo>(new CommandDefinition("""
SELECT id AS Id, title AS Title, created_by AS CreatedBy, folder_id AS FolderId, current_version_id AS CurrentVersionId, coalesce(is_confidential,false) AS IsConfidential
FROM ged.document
WHERE tenant_id=@tenantId AND id=@documentId AND coalesce(reg_status,'A')='A';
""", new { tenantId, documentId }, tx, cancellationToken: ct));

    private async Task<bool> CanMutateAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, DocumentInfo doc, CancellationToken ct)
    {
        if (await _accessPolicy.IsAdminAsync(tenantId, userId, principal, ct)) return true;
        if (principal.IsInRole("HOSPITAL")) return false;
        if (await _accessPolicy.CanMoveDocumentAsync(tenantId, userId, doc.Id, principal, ct)) return true;
        return doc.CreatedBy == userId && !doc.IsConfidential;
    }

    private async Task TryEnqueueIndexAsync(Guid tenantId, Guid userId, Guid documentId, Guid? versionId, CancellationToken ct)
    {
        if (!versionId.HasValue) return;
        try { await _processingJobs.EnqueueAsync(tenantId, documentId, versionId.Value, null, null, "SMART_INDEX", 7, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Falha ao enfileirar SMART_INDEX após bulk action. Tenant={TenantId} Document={DocumentId}", tenantId, documentId); }
    }

    private static List<Guid> NormalizeIds(IReadOnlyList<Guid>? ids) => (ids ?? Array.Empty<Guid>()).Where(x => x != Guid.Empty).Distinct().Take(MaxDocumentsPerOperation + 1).ToList();
    private static BulkDocumentActionResponse Failure(BulkDocumentActionRequest request, string message) => new() { Success = false, Requested = request.DocumentIds?.Count ?? 0, Failed = request.DocumentIds?.Count ?? 0, Message = message };
    private async Task AddFailureAsync(BulkDocumentActionResponse response, Guid tenantId, Guid userId, string action, Guid documentId, string? title, string message, CancellationToken ct)
    {
        response.Items.Add(new BulkDocumentActionItemResult { DocumentId = documentId, Title = title, Success = false, Message = message });
        await _audit.WriteAsync(tenantId, userId, action, "GED_DOCUMENT", documentId, message, null, null, new { title }, ct);
    }

    private sealed class DocumentInfo
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public Guid? CreatedBy { get; set; }
        public Guid? FolderId { get; set; }
        public Guid? CurrentVersionId { get; set; }
        public bool IsConfidential { get; set; }
    }
}
