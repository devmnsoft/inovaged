using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        if (request.ContainsSensitiveData && !AiEndpointPolicy.IsLocal(provider)
            && (!_provider.SensitiveDataPermitted || !policy.AllowSensitiveExternalProcessing || string.IsNullOrWhiteSpace(policy.ExternalAuthorizationReference)))
            return Failure(AiFailureKind.Disabled, "Envio externo de dados sensíveis bloqueado: falta autorização formal na política do cliente.", provider, model);

        using var egressMetadata = request.ContainsSensitiveData ? EgressMetadata(request, policy, provider) : null;
        if (egressMetadata is not null) request = request with { ExecutionMetadata = egressMetadata };

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
        // Bind the durable local receipt before a provider can receive the document.
        // Replays also expose the same lease id; the callback must never initiate a send.
        if(request.OnExecutionReserved is not null)
        {
            try { await request.OnExecutionReserved(lease.ExecutionId,cancellationToken); }
            catch(Exception ex)
            {
                if(lease.IsOwner)
                    await _governance.CompleteAsync(lease.ExecutionId,
                        Failure(ex is OperationCanceledException?AiFailureKind.Cancelled:AiFailureKind.Internal,
                            "Não foi possível registrar a execução antes do envio.",provider,model,lease.ExecutionId.ToString("N")),
                        reservation,TimeSpan.Zero,CancellationToken.None);
                throw;
            }
        }
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
        var governed = request with
        {
            OnRequestSent = ct => _governance.MarkSentAsync(lease.ExecutionId, ct),
            SensitiveExternalAuthorizationReference = policy.AllowSensitiveExternalProcessing ? policy.ExternalAuthorizationReference : null
        };
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

    private static JsonDocument EgressMetadata(AiRequest request, AiEffectivePolicy policy, string provider)
    {
        var original = request.ExecutionMetadata is null ? null : JsonNode.Parse(request.ExecutionMetadata.RootElement.GetRawText());
        var metadata = original as JsonObject ?? new JsonObject { ["requestMetadata"] = original };
        metadata["egressPolicy"] = new JsonObject
        {
            ["sensitiveData"] = true, ["localProvider"] = AiEndpointPolicy.IsLocal(provider),
            ["policyRevision"] = policy.Revision,
            ["authorizationReference"] = AiEndpointPolicy.IsLocal(provider) ? null : policy.ExternalAuthorizationReference
        };
        return JsonDocument.Parse(metadata.ToJsonString());
    }
}
