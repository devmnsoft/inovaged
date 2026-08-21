using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Commercial;

namespace InovaGed.Web.Services.Commercial;

public sealed class PublicCommercialService(IDbConnectionFactory db, IConfiguration configuration) : IPublicCommercialService
{
    private static readonly PublicPlan[] FallbackPlans =
    [
        new("FREE", "Grátis", 0, 1, 100, "Essencial", "Não", "Não", "Não", "1", "1", "Não", "Base de conhecimento"),
        new("START", "Start", 297, 3, 500, "Executivo", "Sim", "Assistida", "Setorial", "1", "3", "Não", "E-mail"),
        new("GROWTH", "Growth", 697, 10, 2500, "Completo", "Sim", "Avançada", "Comparativo", "3", "10", "API", "Prioritário"),
        new("ENTERPRISE", "Enterprise", 0, int.MaxValue, int.MaxValue, "Sob medida", "Sim", "Avançada", "Customizado", "Ilimitadas", "Ilimitados", "Completas", "Consultivo")
    ];

    public async Task<IReadOnlyList<PublicPlan>> GetPlansAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        const string sql = """select code, name, monthly_price MonthlyPrice, diagnostics_limit Diagnostics, responses_limit Responses, reports, certificates, intelligence, benchmark, units, users, integrations, support, is_active IsActive from commercial_plans where is_active order by display_order""";
        try { return (await conn.QueryAsync<PublicPlan>(new CommandDefinition(sql, cancellationToken: ct))).AsList(); }
        catch { return FallbackPlans; } // portal remains available while a deployment migration is pending
    }

    public async Task<SignupResult> SignupAsync(SignupViewModel m, string hash, string? ip, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var email = m.Email.Trim().ToLowerInvariant();
        if (await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from commercial_users where normalized_email=@email)", new { email }, tx, cancellationToken: ct)))
            throw new InvalidOperationException("EMAIL_ALREADY_REGISTERED");
        if (!string.IsNullOrWhiteSpace(m.Cnpj) && await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from commercial_organizations where cnpj=@cnpj)", new { cnpj = Digits(m.Cnpj) }, tx, cancellationToken: ct)))
            throw new InvalidOperationException("ORGANIZATION_ALREADY_REGISTERED");

        var orgId = Guid.NewGuid(); var userId = Guid.NewGuid(); var subscriptionId = Guid.NewGuid();
        var tenantCode = Slug(m.OrganizationName) + "-" + orgId.ToString("N")[..6];
        var trialDays = Math.Clamp(configuration.GetValue("Commercial:TrialDays", 14), 1, 90);
        var ends = DateTimeOffset.UtcNow.AddDays(trialDays);
        const string organizationSql = "insert into commercial_organizations(id,name,cnpj,tenant_code,onboarding_status,created_at) values(@orgId,@name,@cnpj,@tenantCode,'pending',now())";
        await conn.ExecuteAsync(new CommandDefinition(organizationSql, new { orgId, name=m.OrganizationName.Trim(), cnpj=Digits(m.Cnpj), tenantCode }, tx, cancellationToken:ct));
        const string userSql = "insert into commercial_users(id,organization_id,name,normalized_email,email,phone,job_title,password_hash,role,email_confirmed,created_at) values(@userId,@orgId,@name,@email,@email,@phone,@jobTitle,@hash,'organization_admin',false,now())";
        await conn.ExecuteAsync(new CommandDefinition(userSql, new { userId, orgId, name=m.Name.Trim(), email, phone=m.Phone.Trim(), jobTitle=m.JobTitle.Trim(), hash }, tx, cancellationToken:ct));
        const string subSql = "insert into trial_subscriptions(id,organization_id,plan_code,status,starts_at,ends_at,created_at) values(@subscriptionId,@orgId,@plan,'trialing',now(),@ends,now())";
        await conn.ExecuteAsync(new CommandDefinition(subSql, new { subscriptionId, orgId, plan=m.PlanCode.ToUpperInvariant(), ends }, tx, cancellationToken:ct));
        var confirmation = Token();
        await conn.ExecuteAsync(new CommandDefinition("insert into email_confirmations(id,user_id,token_hash,expires_at,created_at) values(@id,@userId,@token,now()+interval '24 hours',now())", new { id=Guid.NewGuid(), userId, token=Hash(confirmation) }, tx, cancellationToken:ct));
        await conn.ExecuteAsync(new CommandDefinition("insert into onboarding_states(id,organization_id,user_id,current_step,status,created_at) values(@id,@orgId,@userId,'organization','pending',now())", new { id=Guid.NewGuid(),orgId,userId }, tx,cancellationToken:ct));
        await conn.ExecuteAsync(new CommandDefinition("insert into public_signup_attempts(id,email,ip_address,succeeded,created_at) values(@id,@email,@ip,true,now())",new{id=Guid.NewGuid(),email,ip},tx,cancellationToken:ct));
        await conn.ExecuteAsync(new CommandDefinition("insert into commercial_outbox(id,event_type,recipient,payload,created_at) values(@id,'welcome',@email,@payload::jsonb,now())",new{id=Guid.NewGuid(),email,payload=System.Text.Json.JsonSerializer.Serialize(new{m.Name,tenantCode,confirmation})},tx,cancellationToken:ct));
        await conn.ExecuteAsync(new CommandDefinition("insert into commercial_events(id,organization_id,event_type,payload,created_at) values(@id,@orgId,'signup.completed',@payload::jsonb,now())",new{id=Guid.NewGuid(),orgId,payload=System.Text.Json.JsonSerializer.Serialize(new{plan=m.PlanCode,ip})},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(orgId,userId,ends,tenantCode);
    }

    public async Task<Guid> CreateLeadAsync(DemoRequestViewModel m, string? ip, CancellationToken ct)
    {
        await using var conn=await db.OpenAsync(ct); var id=Guid.NewGuid();
        const string sql="insert into commercial_leads(id,name,company,email,phone,job_title,company_size,main_interest,message,status,origin,ip_address,created_at) values(@id,@Name,@Company,lower(@Email),@Phone,@JobTitle,@CompanySize,@MainInterest,@Message,'new','public_demo',@ip,now())";
        await conn.ExecuteAsync(new CommandDefinition(sql,new{id,m.Name,m.Company,m.Email,m.Phone,m.JobTitle,m.CompanySize,m.MainInterest,m.Message,ip},cancellationToken:ct));
        await conn.ExecuteAsync(new CommandDefinition("insert into commercial_outbox(id,event_type,recipient,payload,created_at) values(@oid,'demo.requested',@recipient,@payload::jsonb,now())",new{oid=Guid.NewGuid(),recipient=configuration["Commercial:SalesEmail"]??"comercial@valoragroup.com.br",payload=System.Text.Json.JsonSerializer.Serialize(new{id,m.Name,m.Company,m.Email})},cancellationToken:ct));
        return id;
    }

    public async Task<string?> CreatePasswordResetAsync(string email,string? ip,CancellationToken ct)
    { await using var conn=await db.OpenAsync(ct); var userId=await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("select id from commercial_users where normalized_email=lower(@email) and active",new{email},cancellationToken:ct)); if(userId is null)return null; var token=Token(); await conn.ExecuteAsync(new CommandDefinition("insert into password_reset_tokens(id,user_id,token_hash,expires_at,requested_ip,created_at) values(@id,@userId,@hash,now()+interval '1 hour',@ip,now())",new{id=Guid.NewGuid(),userId,hash=Hash(token),ip},cancellationToken:ct)); return token; }
    public async Task<bool> ResetPasswordAsync(string token,string passwordHash,CancellationToken ct)
    { await using var conn=await db.OpenAsync(ct); var id=await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("select user_id from password_reset_tokens where token_hash=@hash and used_at is null and expires_at>now() for update",new{hash=Hash(token)},cancellationToken:ct)); if(id is null)return false; await conn.ExecuteAsync(new CommandDefinition("update commercial_users set password_hash=@passwordHash where id=@id; update password_reset_tokens set used_at=now() where token_hash=@hash",new{id,passwordHash,hash=Hash(token)},cancellationToken:ct)); return true; }
    public async Task<IReadOnlyList<CommercialLead>> GetLeadsAsync(string? status,CancellationToken ct) { await using var conn=await db.OpenAsync(ct); return (await conn.QueryAsync<CommercialLead>(new CommandDefinition("select id,name,company,email,status,origin,created_at CreatedAt from commercial_leads where (@status is null or status=@status) order by created_at desc",new{status},cancellationToken:ct))).AsList(); }
    public async Task UpdateLeadAsync(Guid id,string status,string? note,Guid? userId,CancellationToken ct) { var allowed=new[]{"new","contacted","converted","lost"}; if(!allowed.Contains(status))throw new ArgumentOutOfRangeException(nameof(status)); await using var conn=await db.OpenAsync(ct); await conn.ExecuteAsync(new CommandDefinition("update commercial_leads set status=@status,updated_at=now() where id=@id; insert into lead_notes(id,lead_id,note,created_by,created_at) select gen_random_uuid(),@id,@note,@userId,now() where nullif(trim(@note),'') is not null",new{id,status,note,userId},cancellationToken:ct)); }
    private static string? Digits(string? v)=>string.IsNullOrWhiteSpace(v)?null:new string(v.Where(char.IsDigit).ToArray());
    private static string Slug(string v)=>new string(v.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c=>char.IsLetterOrDigit(c)||c==' ').Select(c=>c==' '?'-':c).Take(40).ToArray()).Trim('-');
    private static string Token()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+','-').Replace('/','_').TrimEnd('=');
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
