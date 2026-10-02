using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Common.Database;
using Npgsql;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

/// <summary>PostgreSQL transaction boundary for leases, quota settlement and private derived results.</summary>
public sealed class PostgresAiGovernanceStore(IDbConnectionFactory db) : IAiGovernanceStore
{
    public async Task<AiEffectivePolicy?> GetEffectivePolicyAsync(Guid tenantId, AiTask task, CancellationToken ct)
    {
        const string sql = """select p.tenant_id "TenantId",p.revision "Revision",p.enabled "Enabled",p.allowed_tasks::text "TasksJson",p.allowed_providers::text "ProvidersJson",p.task_models::text "ModelsJson",p.monthly_token_limit "MonthlyTokenLimit",p.maximum_input_characters "MaximumInputCharacters",date_trunc('month',now()) "PeriodStart",coalesce(u.consumed_tokens,0) "ConsumedTokens",coalesce(u.reserved_tokens,0) "ReservedTokens" from ged.ai_tenant_policy p left join ged.ai_monthly_usage u on u.tenant_id=p.tenant_id and u.period_start=date_trunc('month',now())::date where p.tenant_id=@tenantId and p.enabled and p.allowed_tasks ? @task""";
        await using var c=await db.OpenAsync(ct); var r=await c.QuerySingleOrDefaultAsync<PolicyRow>(new CommandDefinition(sql,new{tenantId,task=task.ToString()},cancellationToken:ct)); if(r is null)return null;
        var tasks=JsonSerializer.Deserialize<string[]>(r.TasksJson)??[]; var providers=JsonSerializer.Deserialize<string[]>(r.ProvidersJson)??[]; var models=JsonSerializer.Deserialize<Dictionary<string,string>>(r.ModelsJson)??[];
        return new(r.TenantId,r.Revision,r.Enabled,tasks.Select(x=>Enum.TryParse<AiTask>(x,true,out var v)?v:(AiTask?)null).Where(x=>x.HasValue).Select(x=>x!.Value).ToArray(),providers,models.Where(x=>Enum.TryParse<AiTask>(x.Key,true,out _)).ToDictionary(x=>Enum.Parse<AiTask>(x.Key,true),x=>x.Value),r.MonthlyTokenLimit,r.MaximumInputCharacters,r.PeriodStart,r.ConsumedTokens,r.ReservedTokens);
    }

    public async Task<AiExecutionLease> ReserveAsync(AiRequest request,string provider,string model,long revision,long estimatedTokens,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.IdempotencyKey.Length>128)throw new InvalidOperationException("Uma chave de idempotência válida é obrigatória.");
        var fingerprint=Fingerprint(request,provider,model,revision); await using var c=await db.OpenAsync(ct); await using var tx=await c.BeginTransactionAsync(ct);
        var id=Guid.NewGuid(); var period=DateTime.UtcNow.Date.AddDays(1-DateTime.UtcNow.Day);
        try {
            await c.ExecuteAsync(new CommandDefinition("insert into ged.ai_execution(id,tenant_id,user_id,task,provider,model,idempotency_key,input_fingerprint,policy_revision,state,reserved_tokens,reservation_period,document_refs,expires_at) values(@id,@TenantId,@UserId,@task,@provider,@model,@IdempotencyKey,@fingerprint,@revision,'Reserved',@estimatedTokens,@period,cast(@documents as jsonb),now()+interval '10 minutes')",new{id,request.TenantId,request.UserId,task=request.Task.ToString(),provider,model,request.IdempotencyKey,fingerprint,revision,estimatedTokens,period,documents=JsonSerializer.Serialize(request.Context.Select(x=>x.Reference))},tx,cancellationToken:ct));
        } catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.UniqueViolation) {
            await tx.RollbackAsync(ct); await using var read=await db.OpenAsync(ct); var existing=await read.QuerySingleAsync<ExecutionRow>(new CommandDefinition("select id \"ExecutionId\",state \"State\",input_fingerprint \"Fingerprint\",result_json::text \"ResultJson\" from ged.ai_execution where tenant_id=@TenantId and user_id=@UserId and task=@task and idempotency_key=@IdempotencyKey",new{request.TenantId,request.UserId,task=request.Task.ToString(),request.IdempotencyKey},cancellationToken:ct));
            if(!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(existing.Fingerprint),Encoding.ASCII.GetBytes(fingerprint)))throw new AiIdempotencyConflictException("A chave de idempotência já foi usada com conteúdo diferente.");
            return new(existing.ExecutionId,false,Enum.Parse<AiExecutionState>(existing.State,true),DeserializeResult(existing.ResultJson));
        }
        await c.ExecuteAsync(new CommandDefinition("insert into ged.ai_monthly_usage(tenant_id,period_start) values(@TenantId,@period) on conflict do nothing",new{request.TenantId,period},tx,cancellationToken:ct));
        var reserved=await c.ExecuteScalarAsync<bool>(new CommandDefinition("update ged.ai_monthly_usage u set reserved_tokens=reserved_tokens+@estimatedTokens,updated_at=now() from ged.ai_tenant_policy p where u.tenant_id=@TenantId and u.period_start=@period and p.tenant_id=u.tenant_id and p.enabled and p.revision=@revision and p.allowed_tasks ? @task and p.allowed_providers ? @provider and p.task_models->>@task=@model and u.consumed_tokens+u.reserved_tokens+@estimatedTokens<=p.monthly_token_limit returning true",new{request.TenantId,period,estimatedTokens,revision,task=request.Task.ToString(),provider,model},tx,cancellationToken:ct));
        if(!reserved){await tx.RollbackAsync(ct);throw new InvalidOperationException("A política mudou ou a cota mensal foi atingida.");}
        await tx.CommitAsync(ct); return new(id,true,AiExecutionState.Reserved);
    }

    public async Task<bool> MarkRunningAsync(Guid executionId,CancellationToken ct){await using var c=await db.OpenAsync(ct);return await c.ExecuteScalarAsync<bool>(new CommandDefinition("update ged.ai_execution e set state='Running',started_at=now(),sent_at=now() from ged.ai_tenant_policy p where e.id=@executionId and e.state='Reserved' and p.tenant_id=e.tenant_id and p.enabled and p.revision=e.policy_revision and p.allowed_tasks ? e.task and p.allowed_providers ? e.provider and p.task_models->>e.task=e.model returning true",new{executionId},cancellationToken:ct));}

    public async Task CompleteAsync(Guid executionId,AiResult result,long reservedTokens,TimeSpan duration,CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);var state=result.Success?"Completed":result.Failure switch{AiFailureKind.Timeout=>"RemoteOutcomeUnknown",AiFailureKind.Cancelled=>"RemoteOutcomeUnknown",AiFailureKind.InvalidOutput=>"Rejected",_=>"Failed"};
        // Missing metering is unknown, not asserted as zero. Conservatively settle the reservation.
        var used=result.Usage?.TotalTokens is >0?result.Usage.TotalTokens.Value:result.Failure==AiFailureKind.Disabled?0:reservedTokens;
        var changed=await c.QuerySingleOrDefaultAsync<SettlementRow>(new CommandDefinition("update ged.ai_execution set state=@state,completed_at=now(),duration_ms=@duration,reported_input_tokens=@input,reported_output_tokens=@output,reported_total_tokens=@reported,settled_tokens=@used,usage_estimated=@estimated,result_json=cast(@resultJson as jsonb),result_expires_at=now()+interval '30 days',failure_kind=@failure,limitation=@limitation,correlation_id=@correlation where id=@executionId and state in ('Reserved','Running') and settled_at is null returning tenant_id \"TenantId\",reservation_period \"Period\",reserved_tokens \"Reserved\"",new{executionId,state,duration=(long)duration.TotalMilliseconds,input=result.Usage?.InputTokens,output=result.Usage?.OutputTokens,reported=result.Usage?.TotalTokens,used,estimated=result.Usage?.TotalTokens is null,resultJson=SerializeResult(result),failure=result.Failure.ToString(),result.Limitation,correlation=result.CorrelationId},tx,cancellationToken:ct));
        if(changed is not null){await c.ExecuteAsync(new CommandDefinition("update ged.ai_monthly_usage set reserved_tokens=greatest(0,reserved_tokens-@Reserved),consumed_tokens=consumed_tokens+@used,updated_at=now() where tenant_id=@TenantId and period_start=@Period",new{changed.Reserved,used,changed.TenantId,changed.Period},tx,cancellationToken:ct));await c.ExecuteAsync(new CommandDefinition("update ged.ai_execution set settled_at=now() where id=@executionId",new{executionId},tx,cancellationToken:ct));} await tx.CommitAsync(ct);
    }

    public async Task<AiExecutionStatus?> GetExecutionAsync(Guid tenantId,Guid userId,Guid executionId,CancellationToken ct){await using var c=await db.OpenAsync(ct);var r=await c.QuerySingleOrDefaultAsync<StatusRow>(new CommandDefinition("select id \"ExecutionId\",tenant_id \"TenantId\",user_id \"UserId\",task \"Task\",state \"State\",created_at \"CreatedAt\",completed_at \"CompletedAt\",case when result_expires_at>now() then result_json::text end \"ResultJson\" from ged.ai_execution where id=@executionId and tenant_id=@tenantId and user_id=@userId",new{tenantId,userId,executionId},cancellationToken:ct));return r is null?null:new(r.ExecutionId,r.TenantId,r.UserId,Enum.Parse<AiTask>(r.Task,true),Enum.Parse<AiExecutionState>(r.State,true),r.CreatedAt,r.CompletedAt,DeserializeResult(r.ResultJson));}
    public async Task<int> ExpireReservationsAsync(CancellationToken ct){await using var c=await db.OpenAsync(ct);return await c.ExecuteScalarAsync<int>(new CommandDefinition("select ged.expire_ai_reservations()",cancellationToken:ct));}
    private static string Fingerprint(AiRequest r,string p,string m,long revision){var canonical=JsonSerializer.Serialize(new{r.TenantId,r.UserId,Task=r.Task.ToString(),r.Instructions,Context=r.Context.Select(x=>new{x.Reference,x.Text,x.MediaType,Data=x.Data is null?null:Convert.ToHexString(SHA256.HashData(x.Data))}),Provider=p,Model=m,revision,Schema=r.OutputSchema?.RootElement.GetRawText()});return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();}
    private static string SerializeResult(AiResult r)=>JsonSerializer.Serialize(new StoredResult(r.Success,r.Text,r.StructuredData?.RootElement.GetRawText(),r.Usage,r.Provider,r.Model,r.Failure,r.Limitation,r.CorrelationId));
    private static AiResult? DeserializeResult(string? json){if(string.IsNullOrWhiteSpace(json))return null;var r=JsonSerializer.Deserialize<StoredResult>(json);return r is null?null:new(r.Success,r.Text,r.Structured is null?null:JsonDocument.Parse(r.Structured),r.Usage,r.Provider,r.Model,r.Failure,r.Limitation,r.CorrelationId);}
    private sealed record StoredResult(bool Success,string? Text,string? Structured,AiUsage? Usage,string Provider,string Model,AiFailureKind Failure,string? Limitation,string? CorrelationId);
    private sealed class PolicyRow{public Guid TenantId{get;set;}public long Revision{get;set;}public bool Enabled{get;set;}public string TasksJson{get;set;}="[]";public string ProvidersJson{get;set;}="[]";public string ModelsJson{get;set;}="{}";public long MonthlyTokenLimit{get;set;}public int MaximumInputCharacters{get;set;}public DateTimeOffset PeriodStart{get;set;}public long ConsumedTokens{get;set;}public long ReservedTokens{get;set;}}
    private sealed class ExecutionRow{public Guid ExecutionId{get;set;}public string State{get;set;}="";public string Fingerprint{get;set;}="";public string? ResultJson{get;set;}}
    private sealed class SettlementRow{public Guid TenantId{get;set;}public DateTime Period{get;set;}public long Reserved{get;set;}}
    private sealed class StatusRow{public Guid ExecutionId{get;set;}public Guid TenantId{get;set;}public Guid UserId{get;set;}public string Task{get;set;}="";public string State{get;set;}="";public DateTimeOffset CreatedAt{get;set;}public DateTimeOffset? CompletedAt{get;set;}public string? ResultJson{get;set;}}
}
