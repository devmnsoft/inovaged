using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;

namespace InovaGed.Application.RetentionCases;

public sealed class RetentionCaseExecutionService
{
    private readonly IRetentionCaseRepository _cases;
    private readonly IRetentionCaseExecutionRepository _execRepo;
    private readonly IRetentionAuditWriter _audit;
    private readonly ILogger<RetentionCaseExecutionService> _logger;

    public RetentionCaseExecutionService(
        IRetentionCaseRepository cases,
        IRetentionCaseExecutionRepository execRepo,
        IRetentionAuditWriter audit,
        ILogger<RetentionCaseExecutionService> logger)
    {
        _cases = cases;
        _execRepo = execRepo;
        _audit = audit;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid tenantId, Guid userId, Guid caseId, CancellationToken ct)
    {
        var data = await _cases.GetAsync(tenantId, caseId, ct);
        if (data is null) throw new InvalidOperationException("Caso não encontrado.");

        var (c, items) = data.Value;

        if (!string.Equals(c.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Somente casos APPROVED podem ser executados.");

        // Executa só os itens APPROVE
        var approvedItems = items.Where(x => x.Decision == "APPROVE").ToList();
        if (approvedItems.Count == 0)
            throw new InvalidOperationException("Não há itens APPROVE para executar.");

        _logger.LogInformation("Execute retention case START. Tenant={TenantId} Case={CaseId} Items={Count}", tenantId, caseId, approvedItems.Count);

        // Execução transacional por lote no repositório
        var result = await _execRepo.ExecuteCaseAsync(tenantId, userId, caseId, ct);

        // Auditoria
        await _audit.WriteAsync(tenantId, userId, caseId, "CASE_EXECUTED", $"ExecutedItems={result.ExecutedItems} BlockedItems={result.BlockedItems}", ct);

        _logger.LogInformation("Execute retention case END. Tenant={TenantId} Case={CaseId} Executed={Executed} Blocked={Blocked}",
            tenantId, caseId, result.ExecutedItems, result.BlockedItems);
    }
}