using System.Text.Json;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Common.Database;
using Npgsql;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

public sealed class PostgresAiGovernanceStore : IAiGovernanceStore
{
    private readonly IDbConnectionFactory _db;
    public PostgresAiGovernanceStore(IDbConnectionFactory db) => _db = db;

    public async Task<AiEffectivePolicy?> GetEffectivePolicyAsync(Guid tenantId, AiTask task, CancellationToken ct)
    {
        const string sql = """
select p.tenant_id "TenantId", p.revision "Revision", p.enabled "Enabled", p.allowed_tasks "TasksJson",
       p.allowed_providers "ProvidersJson", p.task_models "ModelsJson", p.monthly_token_limit "MonthlyTokenLimit",
       p.maximum_input_characters "MaximumInputCharacters", date_trunc('month', now()) "PeriodStart",
       coalesce(u.consumed_tokens,0) "ConsumedTokens", coalesce(u.reserved_tokens,0) "ReservedTokens"
from ged.ai_tenant_policy p
left join ged.ai_monthly_usage u on u.tenant_id=p.tenant_id and u.period_start=date_trunc('month', now())::date
where p.tenant_id=@tenantId and p.enabled=true and p.allowed_tasks ? @task;
""";
        await using var connection = await _db.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<PolicyRow>(new CommandDefinition(sql, new { tenantId, task = task.ToString() }, cancellationToken: ct));
        if (row is null) return null;
        var tasks = JsonSerializer.Deserialize<string[]>(row.TasksJson) ?? [];
        var providers = JsonSerializer.Deserialize<string[]>(row.ProvidersJson) ?? [];
        var models = JsonSerializer.Deserialize<Dictionary<string, string>>(row.ModelsJson) ?? [];
        return new(row.TenantId, row.Revision, row.Enabled,
            tasks.Select(x => Enum.TryParse<AiTask>(x, true, out var value) ? value : (AiTask?)null).Where(x => x.HasValue).Select(x => x!.Value).ToArray(),
            providers, models.Where(x => Enum.TryParse<AiTask>(x.Key, true, out _)).ToDictionary(x => Enum.Parse<AiTask>(x.Key, true), x => x.Value),
            row.MonthlyTokenLimit, row.MaximumInputCharacters, row.PeriodStart, row.ConsumedTokens, row.ReservedTokens);
    }

    public async Task<AiExecutionLease> ReserveAsync(AiRequest request, string provider, string model, long revision, long estimatedTokens, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw new InvalidOperationException("Uma chave de idempotência válida é obrigatória.");
        await using var connection = await _db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var existing = await connection.QuerySingleOrDefaultAsync<ExecutionRow>(new CommandDefinition("select id \"ExecutionId\", state \"State\" from ged.ai_execution where tenant_id=@TenantId and idempotency_key=@IdempotencyKey for update", request, tx, cancellationToken: ct));
        if (existing is not null) { await tx.CommitAsync(ct); return new(existing.ExecutionId, false, Enum.Parse<AiExecutionState>(existing.State, true)); }
        await connection.ExecuteAsync(new CommandDefinition("insert into ged.ai_monthly_usage(tenant_id,period_start) values(@TenantId,date_trunc('month',now())::date) on conflict do nothing", request, tx, cancellationToken: ct));
        var reserved = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
update ged.ai_monthly_usage u set reserved_tokens=reserved_tokens+@estimatedTokens, updated_at=now()
from ged.ai_tenant_policy p where u.tenant_id=@TenantId and p.tenant_id=u.tenant_id and p.enabled
and u.period_start=date_trunc('month',now())::date and u.consumed_tokens+u.reserved_tokens+@estimatedTokens<=p.monthly_token_limit returning true
""", new { request.TenantId, estimatedTokens }, tx, cancellationToken: ct));
        if (!reserved) { await tx.RollbackAsync(ct); throw new InvalidOperationException("A cota mensal do cliente foi atingida."); }
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_execution(id,tenant_id,user_id,task,provider,model,idempotency_key,policy_revision,state,reserved_tokens,document_refs,expires_at)
values(@id,@TenantId,@UserId,@task,@provider,@model,@IdempotencyKey,@revision,'Reserved',@estimatedTokens,cast(@documents as jsonb),now()+interval '10 minutes')
""", new { id, request.TenantId, request.UserId, task=request.Task.ToString(), provider, model, request.IdempotencyKey, revision, estimatedTokens, documents=JsonSerializer.Serialize(request.Context.Select(x => x.Reference)) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return new(id, true, AiExecutionState.Reserved);
    }

    public async Task MarkRunningAsync(Guid executionId, CancellationToken ct)
    { await using var c=await _db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("update ged.ai_execution set state='Running',started_at=now() where id=@executionId and state='Reserved'",new{executionId},cancellationToken:ct)); }

    public async Task CompleteAsync(Guid executionId, AiResult result, long reservedTokens, TimeSpan duration, CancellationToken ct)
    {
        await using var c=await _db.OpenAsync(ct); await using var tx=await c.BeginTransactionAsync(ct);
        var state = result.Success ? "Completed" : result.Failure switch { AiFailureKind.Timeout => "RemoteOutcomeUnknown", AiFailureKind.Cancelled => "Cancelled", AiFailureKind.InvalidOutput => "Rejected", _ => "Failed" };
        var used = Math.Max(0, result.Usage?.TotalTokens ?? 0);
        await c.ExecuteAsync(new CommandDefinition("""
update ged.ai_execution set state=@state,completed_at=now(),duration_ms=@duration,reported_input_tokens=@input,
reported_output_tokens=@output,reported_total_tokens=@used,failure_kind=@failure,limitation=@limitation,correlation_id=@correlation
where id=@executionId and state in ('Reserved','Running')
""",new{executionId,state,duration=(long)duration.TotalMilliseconds,input=result.Usage?.InputTokens,output=result.Usage?.OutputTokens,used,failure=result.Failure.ToString(),result.Limitation,correlation=result.CorrelationId},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
update ged.ai_monthly_usage u set reserved_tokens=greatest(0,reserved_tokens-@reservedTokens), consumed_tokens=consumed_tokens+@used,updated_at=now()
from ged.ai_execution e where e.id=@executionId and u.tenant_id=e.tenant_id and u.period_start=date_trunc('month',now())::date
""",new{executionId,reservedTokens,used},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
    }
    private sealed class PolicyRow { public Guid TenantId{get;set;} public long Revision{get;set;} public bool Enabled{get;set;} public string TasksJson{get;set;}="[]"; public string ProvidersJson{get;set;}="[]"; public string ModelsJson{get;set;}="{}"; public long MonthlyTokenLimit{get;set;} public int MaximumInputCharacters{get;set;} public DateTimeOffset PeriodStart{get;set;} public long ConsumedTokens{get;set;} public long ReservedTokens{get;set;} }
    private sealed class ExecutionRow { public Guid ExecutionId{get;set;} public string State{get;set;}=""; }
}
