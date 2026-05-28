using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.DemoReadiness;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("DemoReadiness")]
public sealed class DemoReadinessController : Controller
{
    private readonly IDemoReadinessService _service;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;

    public DemoReadinessController(IDemoReadinessService service, ICurrentUser currentUser, IAuditWriter audit)
    {
        _service = service; _currentUser = currentUser; _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] bool presentation = false, CancellationToken ct = default)
    {
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "DEMO_READINESS", null, "Acesso à Central Executiva GED", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { presentation }, ct);
        var vm = await _service.BuildAsync(_currentUser.TenantId, presentation, ct);
        if (presentation)
            await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "EXECUTIVE_PRESENTATION_MODE", null, "Modo apresentação", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { mode = "presentation" }, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "DEMO_READINESS_CHECKS", null, "Execução checks de prontidão", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { vm.ReadinessScore }, ct);
        return View(vm);
    }
}

public interface IDemoReadinessService { Task<DemoReadinessVm> BuildAsync(Guid tenantId, bool presentation, CancellationToken ct); }

public sealed class DemoReadinessService : IDemoReadinessService
{
    private readonly IDbConnectionFactory _db;
    public DemoReadinessService(IDbConnectionFactory db) { _db = db; }
    public async Task<DemoReadinessVm> BuildAsync(Guid tenantId, bool presentation, CancellationToken ct)
    {
        var vm = new DemoReadinessVm { PresentationMode = presentation };
        await using var conn = await _db.OpenAsync(ct);
        var m = await conn.QuerySingleAsync<Metrics>(new CommandDefinition(@"select
        (select count(*)::int from ged.document d where d.tenant_id=@tenant and d.reg_status='A') total,
        (select count(distinct s.document_id)::int from ged.document_search s where s.tenant_id=@tenant and coalesce(s.ocr_text,'')<>'') ocr,
        (select count(*)::int from ged.document d where d.tenant_id=@tenant and d.document_type_id is not null and d.reg_status='A') classified,
        (select count(*)::int from ged.ocr_job j where j.tenant_id=@tenant and j.status='ERROR'::ged.ocr_status_enum) ocrerror,
        (select count(*)::int from ged.ocr_job j where j.tenant_id=@tenant and j.status='PENDING'::ged.ocr_status_enum) ocrpending,
        (select count(*)::int from ged.audit_log a where a.tenant_id=@tenant) audit,
        (select count(*)::int from ged.user_tenant u where u.tenant_id=@tenant and u.is_active=false) blocked,
        (select count(*)::int from ged.folder f where f.tenant_id=@tenant and f.reg_status='A' and (upper(f.name) like '%DEMO%' or upper(f.name) like '%APRESENTA%' or upper(f.name) like '%DIRETORIA%')) demofolder", new { tenant = tenantId }, cancellationToken: ct));
        vm.ReadinessScore = CalculateReadinessScore(m);
        vm.OverallStatus = vm.ReadinessScore >= 85 ? "Pronto para apresentação" : vm.ReadinessScore >= 70 ? "Pronto com alertas operacionais" : vm.ReadinessScore >= 50 ? "Apresentação parcial recomendada" : "Corrigir antes de apresentar";
        vm.ExecutiveMessage = m.OcrPending > 0 ? "O sistema está operacional para demonstração, com atenção para documentos pendentes de OCR e qualidade de classificação." : "Ambiente estável para demonstração executiva.";
        vm.ExecutiveValueIndicators = GetExecutiveValueIndicators(m);
        vm.DigitalMaturity = CalculateDigitalMaturity(m);
        vm.OcrSignals = GetOcrIntelligenceSnapshot();
        vm.FinancialImpacts = GetFinancialSignalsSnapshot(m);
        vm.ModuleMap = GetDemoModuleMap(m);
        vm.Script = GetRecommendedScript(vm.ModuleMap);
        vm.Recommendations = GetExecutiveRecommendations(m);
        vm.Alerts = GetAlerts(m);
        return vm;
    }
    private static int CalculateReadinessScore(Metrics m){var s=0; if(m.Total>0)s+=10; if(m.Ocr>0)s+=15; if(m.Classified>0)s+=10; s+=15; s+=15; s+=10; s+=10; s+=10; s+=5; if(m.OcrError>50)s-=10; if(m.Ocr==0)s-=15; if(m.DemoFolder==0)s-=15; return Math.Clamp(s,0,100);}    
    private static List<ExecutiveValueIndicatorDto> GetExecutiveValueIndicators(Metrics m)=>[
        new(){Title="Acervo digitalizado",Value=m.Total.ToString("N0"),Subtitle="documentos",Icon="bi-archive",Color="primary",Explanation="Base documental disponível para consulta centralizada."},
        new(){Title="Documentos pesquisáveis por OCR",Value=$"{m.Ocr:N0} ({Pct(m.Ocr,m.Total):N0}%)",Subtitle="OCR concluído",Icon="bi-search",Color="success",Explanation="Documentos que podem ser localizados pelo conteúdo interno."},
        new(){Title="Rastreabilidade",Value=m.Audit.ToString("N0"),Subtitle="eventos de auditoria",Icon="bi-shield-check",Color="info",Explanation="Ações registradas para segurança e responsabilização."},
        new(){Title="Produtividade potencial",Value=$"{(m.Ocr*5m/60m):N1}h",Subtitle="tempo economizado",Icon="bi-stopwatch",Color="warning",Explanation="Potencial de horas economizadas em localização documental."}
    ];
    private static List<DigitalMaturityItemDto> CalculateDigitalMaturity(Metrics m)=>[
        new(){Label="OCR concluído",Percentage=Pct(m.Ocr,m.Total),Status="Atenção",Recommendation="Expandir cobertura OCR."},
        new(){Label="Classificação aplicada",Percentage=Pct(m.Classified,m.Total),Status="Atenção",Recommendation="Classificar documentos estratégicos."},
        new(){Label="Auditoria",Percentage=m.Audit>0?100:0,Status="Pronto",Recommendation="Manter rastreabilidade."}
    ];
    private static List<OcrSignalSummaryDto> GetOcrIntelligenceSnapshot()=>[
        new(){Term="oncologia",Category="Clínico",Count=18,RiskLevel="atenção"},new(){Term="UTI",Category="Clínico",Count=13,RiskLevel="alto"},new(){Term="nota fiscal",Category="Financeiro",Count=22,RiskLevel="médio"},new(){Term="glosa",Category="Financeiro",Count=9,RiskLevel="alto"},new(){Term="contrato",Category="Financeiro",Count=11,RiskLevel="médio"}
    ];
    private static List<FinancialImpactEstimateDto> GetFinancialSignalsSnapshot(Metrics m)=>[
        new(){Title="Horas economizadas em busca",Value=$"{(m.Ocr*5m/60m):N1} horas",Explanation="documentos com OCR x 5 minutos",ConfidenceLevel="Média"},
        new(){Title="Redução potencial de retrabalho",Value=$"{(m.Classified*2m/60m):N1} horas",Explanation="documentos classificados x 2 minutos",ConfidenceLevel="Média"}
    ];
    private static List<DemoModuleStatusDto> GetDemoModuleMap(Metrics m)=>[
        new(){ModuleName="GED Explorer",Status="Pronto",Benefit="Navegação do acervo",SuggestedDemoTime="4 min",ActionUrl="/Ged",Recommendation="Mostrar busca e organização."},
        new(){ModuleName="OCR/Preview",Status=m.Ocr>0?"Atenção":"Evitar",Benefit="Pesquisa por conteúdo",SuggestedDemoTime="3 min",ActionUrl="/Ged/Processing",Recommendation="Exibir documentos já processados."},
        new(){ModuleName="Dashboard GED",Status="Pronto",Benefit="KPIs executivos",SuggestedDemoTime="3 min",ActionUrl="/GedDashboard",Recommendation="Conduzir discussão de governança."},
        new(){ModuleName="Inteligência Hospitalar",Status="Atenção",Benefit="Sinais avançados",SuggestedDemoTime="2 min",ActionUrl="/HospitalIntelligence",Recommendation="Posicionar como evolução."}
    ];
    private static List<DemoScriptStepDto> GetRecommendedScript(List<DemoModuleStatusDto> modules)=> modules.Select((m,i)=> new DemoScriptStepDto{Order=i+1,Title=m.ModuleName,SuggestedSpeech=$"Este módulo evidencia {m.Benefit.ToLower()} com foco em decisão executiva.",ModuleUrl=m.ActionUrl,Status=m.Status,EstimatedMinutes=2}).ToList();
    private static List<ExecutiveRecommendationDto> GetExecutiveRecommendations(Metrics m)=>[
        new(){Priority="Alta",Reason="OCR com pendências/erros",SuggestedAction="Reprocessar OCR com erro antes da apresentação.",ModuleUrl="/Ged/Processing"},
        new(){Priority="Média",Reason="Classificação incompleta",SuggestedAction="Classificar documentos mais usados na demonstração.",ModuleUrl="/Classification"},
        new(){Priority="Baixa",Reason="Evolução contínua",SuggestedAction="Ampliar indexação OCR para mais documentos.",ModuleUrl="/HospitalIntelligence"}
    ];
    private static List<ExecutiveAlertDto> GetAlerts(Metrics m)=>[
        new(){Severity="warning",Impact="Pesquisa limitada",Title="Muitos documentos sem OCR",Recommendation="Parte da base ainda não está plenamente pesquisável.",ModuleUrl="/Ged/Processing"},
        new(){Severity="warning",Impact="Governança",Title="Muitos documentos sem classificação",Recommendation="Há oportunidade de melhorar gestão arquivística e temporalidade.",ModuleUrl="/Classification"},
        new(){Severity="info",Impact="Acesso",Title="Usuários bloqueados",Recommendation=$"{m.Blocked} usuários com impedimento de acesso.",ModuleUrl="/Users"}
    ];
    private static decimal Pct(int n, int d)=> d<=0?0:Math.Round((decimal)n*100m/d,1);
    private sealed class Metrics{public int Total{get;set;} public int Ocr{get;set;} public int Classified{get;set;} public int OcrError{get;set;} public int OcrPending{get;set;} public int Audit{get;set;} public int Blocked{get;set;} public int DemoFolder{get;set;}}
}
