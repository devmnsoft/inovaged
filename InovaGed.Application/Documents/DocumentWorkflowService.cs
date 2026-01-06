using InovaGed.Application.Identity;
using InovaGed.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace InovaGed.Application.Documents.Workflow;

public sealed class DocumentWorkflowService : IDocumentWorkflowService
{
    private readonly IDocumentWorkflowRepository _repo;
    private readonly ILogger<DocumentWorkflowService> _logger;
    private readonly IPermissionChecker _perm;

    public DocumentWorkflowService(
        IDocumentWorkflowRepository repo,
        IPermissionChecker perm,
        ILogger<DocumentWorkflowService> logger)
    {
        _repo = repo;
        _perm = perm;
        _logger = logger;
    }

    public async Task ChangeStatusAsync(Guid tenantId, Guid? userId, ChangeStatusRequest req, CancellationToken ct)
    {
        try
        {
            if (userId is null || userId == Guid.Empty)
                throw new UnauthorizedAccessException("Usuário não autenticado.");

            // 1) Documento existe + status atual
            var (current, exists) = await _repo.GetStatusAsync(tenantId, req.DocumentId, ct);
            if (!exists) throw new InvalidOperationException("Documento não encontrado.");

            // 2) Permissão por ação/status
            await EnsureAllowedAsync(tenantId, userId.Value, current, req.ToStatus, ct);

            // 3) Regra de transição
            if (!DocumentWorkflow.CanTransition(current, req.ToStatus))
                throw new InvalidOperationException($"Transição inválida: {current} → {req.ToStatus}.");

            // 4) Motivo obrigatório
            if (DocumentWorkflow.RequiresReason(current, req.ToStatus) && string.IsNullOrWhiteSpace(req.Reason))
                throw new InvalidOperationException("Motivo é obrigatório para esta transição.");

            // 5) Persistência
            await _repo.UpdateStatusAsync(tenantId, req.DocumentId, req.ToStatus, userId, ct);

            await _repo.InsertLogAsync(
                tenantId: tenantId,
                documentId: req.DocumentId,
                fromStatus: current,
                toStatus: req.ToStatus,
                reason: req.Reason?.Trim(),
                userId: userId,
                ipAddress: req.IpAddress,
                userAgent: req.UserAgent,
                ct: ct
            );

            _logger.LogInformation("Status alterado. Tenant={TenantId} Doc={DocId} {From}->{To} User={UserId}",
                tenantId, req.DocumentId, current, req.ToStatus, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao mudar status do documento. Tenant={TenantId} Doc={DocId} To={To}",
                tenantId, req.DocumentId, req.ToStatus);
            throw;
        }
    }

    private async Task EnsureAllowedAsync(Guid tenantId, Guid userId, DocumentStatus from, DocumentStatus to, CancellationToken ct)
    {
        // Ajuste os códigos conforme seu padrão real.
        if (from == DocumentStatus.Draft && to == DocumentStatus.InReview)
        {
            await _perm.DemandAsync(tenantId, userId, "perm:GED.Workflow.SendToReview", ct);
            return;
        }

        if (from == DocumentStatus.InReview && (to == DocumentStatus.InSignature || to == DocumentStatus.Draft))
        {
            await _perm.DemandAsync(tenantId, userId, "perm:GED.Workflow.Review", ct);
            return;
        }

        if (from == DocumentStatus.InSignature && (to == DocumentStatus.Published || to == DocumentStatus.InReview))
        {
            await _perm.DemandAsync(tenantId, userId, "perm:GED.Workflow.Sign", ct);
            return;
        }

        if (from == DocumentStatus.Published && to == DocumentStatus.Archived)
        {
            await _perm.DemandAsync(tenantId, userId, "perm:GED.Workflow.Archive", ct);
            return;
        }

        // fallback conservador
        await _perm.DemandAsync(tenantId, userId, "perm:GED.Workflow.Admin", ct);
    }
}
