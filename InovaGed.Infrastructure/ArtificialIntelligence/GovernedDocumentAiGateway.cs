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
    public int MaxOutputTokens => _provider.MaxOutputTokens;

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

        // This is deliberately conservative: input plus the globally configured maximum output is
        // reserved by the provider boundary. It is an estimate, never presented as exact usage.
        var reservation = Math.Max(1L, (inputCharacters + 2L) / 3L) + _provider.MaxOutputTokens;
        AiExecutionLease lease;
        try { lease = await _governance.ReserveAsync(request, provider, model, policy.Revision, reservation, cancellationToken); }
        catch (AiIdempotencyConflictException ex) { return Failure(AiFailureKind.IdempotencyConflict, ex.Message, provider, model); }
        catch (InvalidOperationException ex) { return Failure(AiFailureKind.QuotaExceeded, ex.Message, provider, model); }
        if (!lease.IsOwner)
        {
            if (lease.ResultExpired)
                return Failure(AiFailureKind.InvalidOutput, "O resultado desta execução expirou e não pode ser reutilizado. Envie uma nova solicitação.", provider, model, lease.ExecutionId.ToString("N"), lease.ExecutionId);
            if (lease.ResultMalformed)
                return Failure(AiFailureKind.InvalidOutput, "O resultado persistido está malformado e não foi reutilizado.", provider, model, lease.ExecutionId.ToString("N"), lease.ExecutionId);
            if (lease.ExistingResult is not null)
                return lease.ExistingResult with { ExecutionId = lease.ExecutionId, CorrelationId = lease.ExecutionId.ToString("N") };
            return Failure(AiFailureKind.RateLimited, "Esta solicitação já está em processamento; consulte a execução existente.", provider, model, lease.ExecutionId.ToString("N"), lease.ExecutionId);
        }

        var watch = Stopwatch.StartNew();
        if (!await _governance.MarkRunningAsync(lease.ExecutionId, cancellationToken))
        {
            var blocked = Failure(AiFailureKind.Disabled, "A política foi alterada antes do envio; nenhum conteúdo foi enviado.", provider, model, lease.ExecutionId.ToString("N"));
            await _governance.CompleteAsync(lease.ExecutionId, blocked, reservation, TimeSpan.Zero, CancellationToken.None);
            return blocked;
        }
        // The governance store stamps sent_at at the exact send instant; every earlier failure path
        // settles with zero consumption instead of a fixed over-reservation.
        var governed = request with { OnRequestSent = ct => _governance.MarkSentAsync(lease.ExecutionId, ct) };
        AiResult result;
        try { result = await _provider.ExecuteAsync(governed, cancellationToken); }
        catch (OperationCanceledException)
        {
            result = Failure(AiFailureKind.Cancelled, "A solicitação foi cancelada.", provider, model, lease.ExecutionId.ToString("N"));
            await _governance.CompleteAsync(lease.ExecutionId, result, reservation, watch.Elapsed, CancellationToken.None);
            throw;
        }
        await _governance.CompleteAsync(lease.ExecutionId, result, reservation, watch.Elapsed, CancellationToken.None);
        // The database keeps the provider correlation for its own audit trail; polling by this user
        // must address the execution id that identifies the lease in the governance store.
        return result with { CorrelationId = lease.ExecutionId.ToString("N"), ExecutionId = lease.ExecutionId };
    }

    private static AiResult Failure(AiFailureKind kind, string message, string provider = "", string model = "", string? correlation = null, Guid? executionId = null) =>
        new(false, null, null, null, provider, model, kind, message, correlation ?? Guid.NewGuid().ToString("N"), false, executionId);
}
