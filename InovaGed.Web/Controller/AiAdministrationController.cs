using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.ArtificialIntelligence;

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
        policy ??= new(); policy.LoadSelections();
        policy.ProviderCatalog = _configuration.GetSection(DocumentAiOptions.SectionName).Get<DocumentAiOptions>()?.Providers ?? [];
        policy.CredentialConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable($"{policy.SelectedProvider.ToUpperInvariant()}_API_KEY"));
        return View(policy);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(bool enabled,string provider,string model,string[]? tasks,long revision,long monthlyTokenLimit=100000,int maximumInputCharacters=50000,CancellationToken ct=default)
    {
        var global=_configuration.GetSection(DocumentAiOptions.SectionName).Get<DocumentAiOptions>()??new();
        if(!global.Providers.TryGetValue(provider,out var providerOptions)||!providerOptions.Enabled||!providerOptions.AllowedModels.Contains(model,StringComparer.Ordinal)||!providerOptions.StructuredOutputModels.Contains(model,StringComparer.Ordinal)) return BadRequest("Provedor/modelo não homologado na configuração global.");
        var allowedTasks=(tasks??[]).Where(x=>Enum.TryParse<AiTask>(x,true,out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(monthlyTokenLimit<0||monthlyTokenLimit>10_000_000||maximumInputCharacters<1000||maximumInputCharacters>global.MaximumInputCharacters)return BadRequest("Limites do cliente não podem exceder os limites globais.");
        await using var c=await _db.OpenAsync(ct);
        var changed=await c.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_tenant_policy(tenant_id,enabled,allowed_tasks,allowed_providers,task_models,monthly_token_limit,maximum_input_characters,updated_by)
values(@tenantId,@enabled,cast(@tasksJson as jsonb),cast(@providers as jsonb),cast(@models as jsonb),@monthlyTokenLimit,@maximumInputCharacters,@userId)
on conflict(tenant_id) do update set enabled=excluded.enabled,revision=ged.ai_tenant_policy.revision+1,allowed_tasks=excluded.allowed_tasks,
allowed_providers=excluded.allowed_providers,task_models=excluded.task_models,monthly_token_limit=excluded.monthly_token_limit,
maximum_input_characters=excluded.maximum_input_characters,updated_at=now(),updated_by=excluded.updated_by where ged.ai_tenant_policy.revision=@revision
""",new{tenantId=_user.TenantId,_user.UserId,enabled,tasksJson=JsonSerializer.Serialize(allowedTasks),providers=JsonSerializer.Serialize(new[]{provider}),models=JsonSerializer.Serialize(allowedTasks.ToDictionary(x=>x,_=>model)),monthlyTokenLimit,maximumInputCharacters,revision},cancellationToken:ct));
        if(changed==0)return Conflict("A política foi alterada por outro administrador. Recarregue antes de salvar.");
        TempData["Success"]="Política de IA salva. Resultados anteriores permanecem vinculados à revisão antiga."; return RedirectToAction(nameof(Index));
    }
}
public sealed class AiPolicyVm { public bool Enabled{get;set;} public long Revision{get;set;} public string AllowedTasks{get;set;}="[]"; public string AllowedProviders{get;set;}="[]"; public string TaskModels{get;set;}="{}"; public long MonthlyTokenLimit{get;set;}=100000; public int MaximumInputCharacters{get;set;}=50000; public long ConsumedTokens{get;set;} public long ReservedTokens{get;set;} public long Failures{get;set;} public bool CredentialConfigured{get;set;} public string SelectedProvider{get;set;}="";public string SelectedModel{get;set;}="";public HashSet<string> SelectedTasks{get;set;}=[];public Dictionary<string,AiProviderOptions> ProviderCatalog{get;set;}=[];public void LoadSelections(){SelectedProvider=(JsonSerializer.Deserialize<string[]>(AllowedProviders)??[]).FirstOrDefault()??"";var models=JsonSerializer.Deserialize<Dictionary<string,string>>(TaskModels)??[];SelectedModel=models.Values.FirstOrDefault()??"";SelectedTasks=(JsonSerializer.Deserialize<string[]>(AllowedTasks)??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);}}
