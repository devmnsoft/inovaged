using System.Diagnostics;
using InovaGed.Application.ArtificialIntelligence;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

/// <summary>
/// Fail-closed boundary applied to every external AI call. Policy, idempotency and quota are
/// evaluated in PostgreSQL before the provider receives document text.
/// </summary>
public sealed class GovernedDocumentAiGateway : IDocumentAiGateway
{
    private readonly DocumentAiGateway _provider;
    private readonly IAiGovernanceStore _governance;
    public GovernedDocumentAiGateway(DocumentAiGateway provider, IAiGovernanceStore governance)
    { _provider = provider; _governance = governance; }
    public IReadOnlyDictionary<string, AiCapabilities> Capabilities => _provider.Capabilities;

    public async Task<AiResult> ExecuteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var policy = await _governance.GetEffectivePolicyAsync(request.TenantId, request.Task, cancellationToken);
        if (policy is null || !policy.Enabled || !policy.Tasks.Contains(request.Task))
            return Failure(AiFailureKind.Disabled, "O cliente não possui política explícita para esta tarefa.");

        var provider = policy.Providers.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(provider) || !policy.Models.TryGetValue(request.Task, out var model))
            return Failure(AiFailureKind.ModelUnavailable, "A política efetiva não possui provedor e modelo autorizados.");
        if (!provider.Equals(_provider.ActiveProvider, StringComparison.OrdinalIgnoreCase) || !string.Equals(model, _provider.ModelFor(request.Task), StringComparison.Ordinal))
            return Failure(AiFailureKind.ModelUnavailable, "A configuração global não autoriza o provedor/modelo definido pelo cliente.", provider, model);

        var inputCharacters = request.Instructions.Length + request.Context.Sum(x => x.Reference.Length + x.Text.Length);
        if (inputCharacters > policy.MaximumInputCharacters)
            return Failure(AiFailureKind.InvalidOutput, "A entrada excede o limite da política do cliente.", provider, model);

        var reservation = Math.Max(1, (inputCharacters + 3L) / 4L);
        AiExecutionLease lease;
        try { lease = await _governance.ReserveAsync(request, provider, model, policy.Revision, reservation, cancellationToken); }
        catch (InvalidOperationException ex) { return Failure(AiFailureKind.QuotaExceeded, ex.Message, provider, model); }
        if (!lease.IsOwner)
            return lease.ExistingResult ?? Failure(AiFailureKind.RateLimited, "Esta solicitação já está em processamento; consulte a execução existente.", provider, model, lease.ExecutionId.ToString("N"));

        var watch = Stopwatch.StartNew();
        await _governance.MarkRunningAsync(lease.ExecutionId, cancellationToken);
        AiResult result;
        try { result = await _provider.ExecuteAsync(request, cancellationToken); }
        catch (OperationCanceledException)
        {
            result = Failure(AiFailureKind.Cancelled, "A solicitação foi cancelada.", provider, model, lease.ExecutionId.ToString("N"));
            await _governance.CompleteAsync(lease.ExecutionId, result, reservation, watch.Elapsed, CancellationToken.None);
            throw;
        }
        await _governance.CompleteAsync(lease.ExecutionId, result, reservation, watch.Elapsed, CancellationToken.None);
        return result;
    }

    private static AiResult Failure(AiFailureKind kind, string message, string provider = "", string model = "", string? correlation = null) =>
        new(false, null, null, null, provider, model, kind, message, correlation ?? Guid.NewGuid().ToString("N"));
}
