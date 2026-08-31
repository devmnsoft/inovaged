using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelTraceTokenService : ILabelTraceTokenService
{
    public LabelTraceToken Generate()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new(token, Hash(token));
    }

    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    public bool IsValid(string token) => !string.IsNullOrWhiteSpace(token) && token.Length is >= 32 and <= 128 && token.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
}

public sealed class LabelTraceabilityService(IDbConnectionFactory factory, ILabelTraceTokenService tokens) : ILabelTraceabilityService
{
    public async Task<LabelTraceIssued> IssueAsync(LabelTraceIssueCommand x, CancellationToken ct)
    {
        var secret = tokens.Generate();
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var sequence = await db.ExecuteScalarAsync<long>(new CommandDefinition("select nextval('ged.label_trace_code_seq')", transaction: tx, cancellationToken: ct));
        var code = $"LBL-{DateTime.UtcNow:yyyy}-{sequence:000000}";
        var id = Guid.NewGuid();
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.label_trace_identity(id,tenant_id,label_print_id,trace_token_hash,trace_code,subject_type,subject_id,template_code,template_version,status,issued_by,issued_by_name,payload_hash)
values(@id,@TenantId,@LabelPrintId,@hash,@code,@SubjectType,@SubjectId,@TemplateCode,@TemplateVersion,'ACTIVE',@IssuedBy,@IssuedByName,@PayloadHash)
""", new { id, x.TenantId, x.LabelPrintId, hash=secret.Hash, code, x.SubjectType, x.SubjectId, x.TemplateCode, x.TemplateVersion, x.IssuedBy, x.IssuedByName, x.PayloadHash }, tx, cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(new(id,code,x.SubjectType,x.TemplateCode,x.TemplateVersion,LabelTraceStatus.Active,DateTime.UtcNow,x.TenantId),secret.Token,$"/l/{secret.Token}");
    }

    public async Task<LabelTracePublicInfo?> ResolvePublicAsync(string token,CancellationToken ct)
    {
        token=Extract(token); if(!tokens.IsValid(token)) return null;
        await using var db=await factory.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<LabelTracePublicInfo>(new CommandDefinition(BaseSql+" where trace_token_hash=@hash and reg_status='A' limit 1",new{hash=tokens.Hash(token)},cancellationToken:ct));
    }

    public async Task<LabelTracePublicInfo?> ResolveInternalAsync(Guid tenantId,string value,CancellationToken ct)
    {
        var token=Extract(value); await using var db=await factory.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<LabelTracePublicInfo>(new CommandDefinition(BaseSql+" where tenant_id=@tenantId and reg_status='A' and (upper(trace_code)=upper(@value) or trace_token_hash=@hash) limit 1",new{tenantId,value=value.Trim(),hash=tokens.IsValid(token)?tokens.Hash(token):""},cancellationToken:ct));
    }

    public async Task RegisterScanAsync(LabelTracePublicInfo t,Guid? userId,string? userName,string source,string result,string? ip,string? agent,string? location,string? notes,CancellationToken ct)
    { await using var db=await factory.OpenAsync(ct); await db.ExecuteAsync(new CommandDefinition("insert into ged.label_scan_event(id,tenant_id,trace_id,scan_source,scanned_by,scanned_by_name,client_ip,user_agent,scan_result,location_hint,notes) values(gen_random_uuid(),@TenantId,@Id,@source,@userId,@userName,@ip,@agent,@result,@location,@notes)",new{t.TenantId,t.Id,source,userId,userName,ip,agent,result,location,notes},cancellationToken:ct)); }

    public async Task<Guid> ReplaceAsync(Guid tenantId,string oldValue,string reason,string template,Guid userId,string? userName,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("O motivo da substituição é obrigatório.");
        var old=await ResolveInternalAsync(tenantId,oldValue,ct) ?? throw new KeyNotFoundException("Etiqueta não localizada.");
        var issued=await IssueAsync(new(tenantId,null,old.SubjectType,null,template,old.TemplateVersion,userId,userName,null),ct);
        await using var db=await factory.OpenAsync(ct); await using var tx=await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("update ged.label_trace_identity set status='REPLACED',replaced_by_trace_id=@newId,replaced_at=now(),replacement_reason=@reason where tenant_id=@tenantId and id=@oldId and status='ACTIVE'",new{newId=issued.Trace.Id,reason,tenantId,oldId=old.Id},tx,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("insert into ged.label_replacement_event(tenant_id,old_trace_id,new_trace_id,reason,requested_by,requested_by_name) values(@tenantId,@oldId,@newId,@reason,@userId,@userName) returning id",new{tenantId,oldId=old.Id,newId=issued.Trace.Id,reason,userId,userName},tx,cancellationToken:ct));
        await tx.CommitAsync(ct); return issued.Trace.Id;
    }

    private const string BaseSql="select id,trace_code TraceCode,subject_type SubjectType,template_code TemplateCode,template_version TemplateVersion,status,issued_at IssuedAt,tenant_id TenantId from ged.label_trace_identity";
    private static string Extract(string value){value=(value??"").Trim();if(Uri.TryCreate(value,UriKind.Absolute,out var u))return u.Segments.LastOrDefault()?.Trim('/')??"";return value.StartsWith("/l/",StringComparison.OrdinalIgnoreCase)?value[3..]:value;}
}
