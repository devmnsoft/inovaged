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
        var id=Guid.NewGuid(); // The monthly bucket is defined in SQL (session timezone) so this write always matches every read of date_trunc('month',now())::date; a C#-side UTC month would drift at the month boundary under a non-UTC connection timezone.
        var period=await c.ExecuteScalarAsync<DateTime>(new CommandDefinition("select date_trunc('month',now())::date",null,tx,cancellationToken:ct));
        try {
            await c.ExecuteAsync(new CommandDefinition("insert into ged.ai_execution(id,tenant_id,user_id,task,provider,model,idempotency_key,input_fingerprint,policy_revision,state,reserved_tokens,reservation_period,document_refs,source_documents,expires_at) values(@id,@TenantId,@UserId,@task,@provider,@model,@IdempotencyKey,@fingerprint,@revision,'Reserved',@estimatedTokens,@period,cast(@documents as jsonb),cast(@sources as jsonb),now()+interval '10 minutes')",new{id,request.TenantId,request.UserId,task=request.Task.ToString(),provider,model,request.IdempotencyKey,fingerprint,revision,estimatedTokens,period,documents=JsonSerializer.Serialize(request.Context.Select(x=>x.Reference)),sources=SerializeSources(request.SourceDocuments)},tx,cancellationToken:ct));
        } catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.UniqueViolation) {
            await tx.RollbackAsync(ct); await using var read=await db.OpenAsync(ct); var existing=await read.QuerySingleAsync<ExecutionRow>(new CommandDefinition("select id \"ExecutionId\",state \"State\",input_fingerprint \"Fingerprint\",case when result_expires_at is null or result_expires_at>now() then result_json::text end \"ResultJson\",(result_expires_at is not null and result_expires_at<=now()) \"ResultExpired\" from ged.ai_execution where tenant_id=@TenantId and user_id=@UserId and task=@task and idempotency_key=@IdempotencyKey",new{request.TenantId,request.UserId,task=request.Task.ToString(),request.IdempotencyKey},cancellationToken:ct));
            if(!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(existing.Fingerprint),Encoding.ASCII.GetBytes(fingerprint)))throw new AiIdempotencyConflictException("A chave de idempotência já foi usada com conteúdo diferente.");
            var replay=ReadStoredResult(existing.ResultJson);
            return new(existing.ExecutionId,false,Enum.Parse<AiExecutionState>(existing.State,true),existing.ResultExpired?null:replay.Result,existing.ResultExpired,replay.Malformed);
        }
        await c.ExecuteAsync(new CommandDefinition("insert into ged.ai_monthly_usage(tenant_id,period_start) values(@TenantId,@period) on conflict do nothing",new{request.TenantId,period},tx,cancellationToken:ct));
        var reserved=await c.ExecuteScalarAsync<bool>(new CommandDefinition("update ged.ai_monthly_usage u set reserved_tokens=reserved_tokens+@estimatedTokens,updated_at=now() from ged.ai_tenant_policy p where u.tenant_id=@TenantId and u.period_start=@period and p.tenant_id=u.tenant_id and p.enabled and p.revision=@revision and p.allowed_tasks ? @task and p.allowed_providers ? @provider and p.task_models->>@task=@model and u.consumed_tokens+u.reserved_tokens+@estimatedTokens<=p.monthly_token_limit returning true",new{request.TenantId,period,estimatedTokens,revision,task=request.Task.ToString(),provider,model},tx,cancellationToken:ct));
        if(!reserved){await tx.RollbackAsync(ct);throw new InvalidOperationException("A política mudou ou a cota mensal foi atingida.");}
        await tx.CommitAsync(ct); return new(id,true,AiExecutionState.Reserved);
    }

    public async Task<bool> MarkRunningAsync(Guid executionId,CancellationToken ct){await using var c=await db.OpenAsync(ct);return await c.ExecuteScalarAsync<bool>(new CommandDefinition("update ged.ai_execution e set state='Running',started_at=now() from ged.ai_tenant_policy p where e.id=@executionId and e.state='Reserved' and p.tenant_id=e.tenant_id and p.enabled and p.revision=e.policy_revision and p.allowed_tasks ? e.task and p.allowed_providers ? e.provider and p.task_models->>e.task=e.model returning true",new{executionId},cancellationToken:ct));}

    // The real send stamp is set only when the governed boundary hands the request to the provider.
    // Expiration uses it to distinguish "never sent, consumed nothing" from "sent, outcome unknown".
    public async Task<bool> MarkSentAsync(Guid executionId,CancellationToken ct){await using var c=await db.OpenAsync(ct);return await c.ExecuteScalarAsync<bool>(new CommandDefinition("update ged.ai_execution set sent_at=coalesce(sent_at,now()) where id=@executionId and state='Running' and settled_at is null returning true",new{executionId},cancellationToken:ct));}

    public async Task CompleteAsync(Guid executionId,AiResult result,long reservedTokens,TimeSpan duration,CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        var state=result.Success?"Completed":result.Failure switch{AiFailureKind.Timeout=>"RemoteOutcomeUnknown",AiFailureKind.Cancelled=>"RemoteOutcomeUnknown",AiFailureKind.InvalidOutput=>"Rejected",_=>"Failed"};
        // Missing metering is unknown, not asserted as zero: a reached provider settles the reservation
        // as estimated usage; a request that never left this host settles at zero.
        var used=result.Usage?.TotalTokens is >0?result.Usage.TotalTokens.Value:result.ProviderReached?reservedTokens:0;
        var row=await c.QuerySingleOrDefaultAsync<SettlementRow>(new CommandDefinition("""
select tenant_id "TenantId",reservation_period "Period",reserved_tokens "Reserved",settled_at "SettledAt",state "State",
       settled_tokens "Settled",usage_estimated "UsageEstimated",sent_at "SentAt",usage_reconciled_at "ReconciledAt",
       (result_json is not null) "HasResult",result_expires_at "ResultExpiresAt"
from ged.ai_execution where id=@executionId for update
""",new{executionId},tx,cancellationToken:ct));
        if(row is null){await tx.CommitAsync(ct);return;}
        if(row.SettledAt is not null)
        {
            await ReconcileLateAsync(c,tx,executionId,row,result,state,duration,ct);
            await tx.CommitAsync(ct);return;
        }
        await c.ExecuteAsync(new CommandDefinition("update ged.ai_execution set state=@state,completed_at=now(),duration_ms=@duration,reported_input_tokens=@input,reported_output_tokens=@output,reported_total_tokens=@reported,settled_tokens=@used,usage_estimated=@estimated,result_json=cast(@resultJson as jsonb),result_expires_at=now()+interval '30 days',failure_kind=@failure,limitation=@limitation,correlation_id=@correlation,settled_at=now() where id=@executionId",new{executionId,state,duration=(long)duration.TotalMilliseconds,input=result.Usage?.InputTokens,output=result.Usage?.OutputTokens,reported=result.Usage?.TotalTokens,used,estimated=result.Usage?.TotalTokens is null,resultJson=SerializeResult(result),failure=result.Failure.ToString(),result.Limitation,correlation=result.CorrelationId},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("update ged.ai_monthly_usage set reserved_tokens=greatest(0,reserved_tokens-@Reserved),consumed_tokens=consumed_tokens+@used,updated_at=now() where tenant_id=@TenantId and period_start=@Period",new{row.Reserved,used,row.TenantId,row.Period},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
    }

    private static async Task ReconcileLateAsync(System.Data.IDbConnection c,System.Data.IDbTransaction tx,Guid executionId,SettlementRow row,AiResult result,string incomingState,TimeSpan duration,CancellationToken ct)
    {
        var protectCompletion=string.Equals(row.State,"Completed",StringComparison.Ordinal);
        var storeResult=result.Success&&!row.HasResult;
        var nextState=protectCompletion&&!result.Success?row.State:result.Success?"Completed":incomingState;
        var refreshExpiry=result.Success&&storeResult&&!row.HasResult;
        await c.ExecuteAsync(new CommandDefinition("""
update ged.ai_execution set
    state=@state,
    completed_at=coalesce(completed_at,now()),
    duration_ms=coalesce(duration_ms,@duration),
    reported_input_tokens=case when @protect and not @success then reported_input_tokens else coalesce(@input,reported_input_tokens) end,
    reported_output_tokens=case when @protect and not @success then reported_output_tokens else coalesce(@output,reported_output_tokens) end,
    reported_total_tokens=case when @protect and not @success then reported_total_tokens else coalesce(@reported,reported_total_tokens) end,
    result_json=case when @storeResult and result_json is null then cast(@resultJson as jsonb) else result_json end,
    result_expires_at=case when @refreshExpiry then now()+interval '30 days' else result_expires_at end,
    failure_kind=case when @protect and not @success then failure_kind else @failure end,
    limitation=case when @protect and not @success then limitation else @limitation end,
    correlation_id=coalesce(correlation_id,@correlation)
where id=@executionId
""",new{executionId,state=nextState,duration=(long)duration.TotalMilliseconds,input=result.Usage?.InputTokens,output=result.Usage?.OutputTokens,reported=result.Usage?.TotalTokens,storeResult,refreshExpiry,resultJson=SerializeResult(result),protect=protectCompletion,success=result.Success,failure=result.Failure.ToString(),result.Limitation,correlation=result.CorrelationId},tx,cancellationToken:ct));
        var reported=result.Usage?.TotalTokens;
        if(reported is null||!row.UsageEstimated||row.SentAt is null||row.ReconciledAt is not null)return;
        var delta=reported.Value-row.Settled;
        var adjusted=await c.ExecuteAsync(new CommandDefinition("update ged.ai_monthly_usage set consumed_tokens=greatest(0,consumed_tokens+@delta),updated_at=now() where tenant_id=@TenantId and period_start=@Period",new{delta,row.TenantId,row.Period},tx,cancellationToken:ct));
        if(adjusted==0)return;
        await c.ExecuteAsync(new CommandDefinition("update ged.ai_execution set usage_reconciled_at=now(),reconciled_delta=@delta where id=@executionId and usage_reconciled_at is null",new{executionId,delta},tx,cancellationToken:ct));
    }

    public async Task<AiExecutionStatus?> GetExecutionAsync(Guid tenantId,Guid userId,Guid executionId,CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct);
        var r=await c.QuerySingleOrDefaultAsync<StatusRow>(new CommandDefinition("""
select id "ExecutionId",tenant_id "TenantId",user_id "UserId",task "Task",state "State",created_at "CreatedAt",completed_at "CompletedAt",
       result_expires_at "ResultExpiresAt",source_documents::text "SourcesJson",document_refs::text "RefsJson",
       case when result_expires_at>now() then result_json::text end "ResultJson"
from ged.ai_execution where id=@executionId and tenant_id=@tenantId and user_id=@userId
""",new{tenantId,userId,executionId},cancellationToken:ct));
        if(r is null)return null;
        var read=AiExecutionSourceCodec.Read(r.SourcesJson,r.RefsJson);
        var stored=ReadStoredResult(r.ResultJson);
        return new(r.ExecutionId,r.TenantId,r.UserId,Enum.Parse<AiTask>(r.Task,true),Enum.Parse<AiExecutionState>(r.State,true),r.CreatedAt,r.CompletedAt,stored.Malformed?null:stored.Result,read.Sources,r.ResultExpiresAt,read.Integrity,stored.Malformed);
    }

    public async Task MigrateVerifiedLegacySourcesAsync(Guid tenantId,Guid userId,Guid executionId,IReadOnlyList<AiExecutionSource> sources,CancellationToken ct)
    {
        if(sources.Count==0)return;
        await using var c=await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("update ged.ai_execution set source_documents=cast(@sources as jsonb) where id=@executionId and tenant_id=@tenantId and user_id=@userId and source_documents='[]'::jsonb",new{executionId,tenantId,userId,sources=SerializeSources(sources)},cancellationToken:ct));
    }
    public async Task<int> ExpireReservationsAsync(CancellationToken ct){await using var c=await db.OpenAsync(ct);return await c.ExecuteScalarAsync<int>(new CommandDefinition("select ged.expire_ai_reservations()",cancellationToken:ct));}
    public async Task<AiRecoveryHealth> GetRecoveryHealthAsync(CancellationToken ct){await using var c=await db.OpenAsync(ct);var r=await c.QuerySingleOrDefaultAsync<HealthRow>(new CommandDefinition("select count(*) filter (where state='RemoteOutcomeUnknown') \"RemoteOutcomeUnknown\",count(*) filter (where state='Expired') \"Expired\",count(*) filter (where state in ('Reserved','Running') and expires_at<now()) \"PendingExpired\" from ged.ai_execution",cancellationToken:ct));return new(r.RemoteOutcomeUnknown,r.Expired,r.PendingExpired);}
    private static string Fingerprint(AiRequest r,string p,string m,long revision){var canonical=JsonSerializer.Serialize(new{r.TenantId,r.UserId,Task=r.Task.ToString(),r.Instructions,Context=r.Context.Select(x=>new{x.Reference,x.Text,x.MediaType,Data=x.Data is null?null:Convert.ToHexString(SHA256.HashData(x.Data))}),Provider=p,Model=m,revision,Schema=r.OutputSchema?.RootElement.GetRawText()});return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();}
    private static string SerializeSources(IReadOnlyList<AiExecutionSource> sources)=>JsonSerializer.Serialize(sources.Select(x=>new{documentId=x.DocumentId.ToString("N"),versionId=x.VersionId.ToString("N")}));
    private static IReadOnlyList<AiExecutionSource> DeserializeSources(string? json){if(string.IsNullOrWhiteSpace(json))return [];try{var list=JsonSerializer.Deserialize<List<SourceRef>>(json)??[];return list.SelectMany(x=>{if(!Guid.TryParse(x.documentId,out var d)||!Guid.TryParse(x.versionId,out var v))return System.Array.Empty<AiExecutionSource>();return new[] { new AiExecutionSource(d,v) };}).ToArray();}catch(JsonException){return [];}}
    private static string SerializeResult(AiResult r)=>JsonSerializer.Serialize(new StoredResult(r.Success,r.Text,r.StructuredData?.RootElement.GetRawText(),r.Usage,r.Provider,r.Model,r.Failure,r.Limitation,r.CorrelationId));
    private readonly record struct StoredRead(AiResult? Result, bool Malformed);
    private static StoredRead ReadStoredResult(string? json)
    {
        if(string.IsNullOrWhiteSpace(json))return new(null,false);
        try
        {
            using var document=JsonDocument.Parse(json);
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return new(null,true);
            var success=root.TryGetProperty("Success",out var successElement)&&successElement.ValueKind==JsonValueKind.True;
            var text=ReadString(root,"Text");
            JsonDocument? structured=null;
            if(root.TryGetProperty("Structured",out var structuredElement)&&structuredElement.ValueKind==JsonValueKind.String)
            {
                var raw=structuredElement.GetString();
                if(!string.IsNullOrWhiteSpace(raw))structured=JsonDocument.Parse(raw);
            }
            var failure=ReadFailure(root);
            if(failure is null)return new(null,true);
            var usage=ReadUsage(root);
            return new(new AiResult(success,text,structured,usage,ReadString(root,"Provider")??"",ReadString(root,"Model")??"",failure.Value,ReadString(root,"Limitation"),ReadString(root,"CorrelationId")),false);
        }
        catch(JsonException){return new(null,true);}
    }
    private static string? ReadString(JsonElement root,string name)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    private static AiFailureKind? ReadFailure(JsonElement root)
    {
        if(!root.TryGetProperty("Failure",out var value))return AiFailureKind.None;
        if(value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)&&Enum.IsDefined(typeof(AiFailureKind),number))return (AiFailureKind)number;
        if(value.ValueKind==JsonValueKind.String&&Enum.TryParse<AiFailureKind>(value.GetString(),true,out var parsed)&&Enum.IsDefined(parsed))return parsed;
        return null;
    }
    private static AiUsage? ReadUsage(JsonElement root)
    {
        if(!root.TryGetProperty("Usage",out var usage)||usage.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)return null;
        if(usage.ValueKind!=JsonValueKind.Object)return null;
        return new(ReadLong(usage,"InputTokens"),ReadLong(usage,"OutputTokens"),ReadLong(usage,"TotalTokens"));
    }
    private static long? ReadLong(JsonElement root,string name)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var number)?number:null;
    private sealed record StoredResult(bool Success,string? Text,string? Structured,AiUsage? Usage,string Provider,string Model,AiFailureKind Failure,string? Limitation,string? CorrelationId);
    // Parameter names must match the serialized source_documents keys exactly (System.Text.Json is case-sensitive).
    private sealed record SourceRef(string? documentId,string? versionId);
    private sealed class PolicyRow{public Guid TenantId{get;set;}public long Revision{get;set;}public bool Enabled{get;set;}public string TasksJson{get;set;}="[]";public string ProvidersJson{get;set;}="[]";public string ModelsJson{get;set;}="{}";public long MonthlyTokenLimit{get;set;}public int MaximumInputCharacters{get;set;}public DateTimeOffset PeriodStart{get;set;}public long ConsumedTokens{get;set;}public long ReservedTokens{get;set;}}
    private sealed class ExecutionRow{public Guid ExecutionId{get;set;}public string State{get;set;}="";public string Fingerprint{get;set;}="";public string? ResultJson{get;set;}public bool ResultExpired{get;set;}}
    private sealed class SettlementRow{public Guid TenantId{get;set;}public DateTime Period{get;set;}public long Reserved{get;set;}public DateTime? SettledAt{get;set;}public string State{get;set;}="";public long Settled{get;set;}public bool UsageEstimated{get;set;}public DateTime? SentAt{get;set;}public DateTime? ReconciledAt{get;set;}public bool HasResult{get;set;}public DateTime? ResultExpiresAt{get;set;}}
    private sealed class StatusRow{public Guid ExecutionId{get;set;}public Guid TenantId{get;set;}public Guid UserId{get;set;}public string Task{get;set;}="";public string State{get;set;}="";public DateTimeOffset CreatedAt{get;set;}public DateTimeOffset? CompletedAt{get;set;}public DateTimeOffset? ResultExpiresAt{get;set;}public string? SourcesJson{get;set;}public string? RefsJson{get;set;}public string? ResultJson{get;set;}}
    private sealed class HealthRow{public int RemoteOutcomeUnknown{get;set;}public int Expired{get;set;}public int PendingExpired{get;set;}}
}
