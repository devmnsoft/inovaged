using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.Administracao)]
public sealed class AiAdministrationController : Controller
{
    private readonly IDbConnectionFactory _db; private readonly ICurrentUser _user; private readonly IConfiguration _configuration;
    public AiAdministrationController(IDbConnectionFactory db, ICurrentUser user, IConfiguration configuration) { _db=db; _user=user; _configuration=configuration; }
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await using var c=await _db.OpenAsync(ct);
        var policy=await c.QuerySingleOrDefaultAsync<AiPolicyVm>(new CommandDefinition("""
select p.enabled "Enabled",p.revision "Revision",p.allowed_tasks::text "AllowedTasks",p.allowed_providers::text "AllowedProviders",
p.task_models::text "TaskModels",p.monthly_token_limit "MonthlyTokenLimit",p.maximum_input_characters "MaximumInputCharacters",
coalesce(u.consumed_tokens,0) "ConsumedTokens",coalesce(u.reserved_tokens,0) "ReservedTokens",
(select count(*) from ged.ai_execution e where e.tenant_id=p.tenant_id and e.state in ('Failed','Rejected','RemoteOutcomeUnknown')) "Failures"
from ged.ai_tenant_policy p left join ged.ai_monthly_usage u on u.tenant_id=p.tenant_id and u.period_start=date_trunc('month',now())::date where p.tenant_id=@tenantId
""",new{tenantId=_user.TenantId},cancellationToken:ct));
        policy ??= new();
        policy.CredentialConfigured = new[]{"GROQ_API_KEY","GEMINI_API_KEY","DEEPSEEK_API_KEY"}.Any(x=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(x)));
        return View(policy);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(bool enabled,string provider,string model,long monthlyTokenLimit=100000,int maximumInputCharacters=50000,CancellationToken ct=default)
    {
        var allowedProviders=new[]{"Groq","Gemini","DeepSeek"}; if(!allowedProviders.Contains(provider,StringComparer.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(model)||model.Length>160) return BadRequest();
        monthlyTokenLimit=Math.Clamp(monthlyTokenLimit,0,10_000_000); maximumInputCharacters=Math.Clamp(maximumInputCharacters,1000,500000);
        await using var c=await _db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_tenant_policy(tenant_id,enabled,allowed_tasks,allowed_providers,task_models,monthly_token_limit,maximum_input_characters,updated_by)
values(@tenantId,@enabled,'["AskCollection","Summarize","ExtractMetadata","SuggestClassification"]',cast(@providers as jsonb),cast(@models as jsonb),@monthlyTokenLimit,@maximumInputCharacters,@userId)
on conflict(tenant_id) do update set enabled=excluded.enabled,revision=ged.ai_tenant_policy.revision+1,allowed_tasks=excluded.allowed_tasks,
allowed_providers=excluded.allowed_providers,task_models=excluded.task_models,monthly_token_limit=excluded.monthly_token_limit,
maximum_input_characters=excluded.maximum_input_characters,updated_at=now(),updated_by=excluded.updated_by
""",new{tenantId=_user.TenantId,_user.UserId,enabled,providers=JsonSerializer.Serialize(new[]{provider}),models=JsonSerializer.Serialize(new Dictionary<string,string>{{"AskCollection",model},{"Summarize",model},{"ExtractMetadata",model},{"SuggestClassification",model}}),monthlyTokenLimit,maximumInputCharacters},cancellationToken:ct));
        TempData["Success"]="Política de IA salva. Resultados anteriores permanecem vinculados à revisão antiga."; return RedirectToAction(nameof(Index));
    }
}
public sealed class AiPolicyVm { public bool Enabled{get;set;} public long Revision{get;set;} public string AllowedTasks{get;set;}="[]"; public string AllowedProviders{get;set;}="[]"; public string TaskModels{get;set;}="{}"; public long MonthlyTokenLimit{get;set;}=100000; public int MaximumInputCharacters{get;set;}=50000; public long ConsumedTokens{get;set;} public long ReservedTokens{get;set;} public long Failures{get;set;} public bool CredentialConfigured{get;set;} }
