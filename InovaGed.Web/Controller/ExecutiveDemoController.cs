using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.ExecutiveDemo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("ExecutiveDemo")]
public sealed class ExecutiveDemoController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _auditWriter;

    public ExecutiveDemoController(ICurrentUser currentUser, IDbConnectionFactory db, IAuditWriter auditWriter)
    {
        _currentUser = currentUser;
        _db = db;
        _auditWriter = auditWriter;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!User.IsInRole(AppRoles.Admin))
        {
            await _auditWriter.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "ACCESS_DENIED", "EXECUTIVE_DEMO", null, "Tentativa de acesso negada ao modo executivo", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { denied = true }, ct);
            return RedirectToAction("AccessDenied", "Account");
        }

        var vm = new ExecutiveDemoViewModel
        {
            Overview = await LoadOverviewAsync(ct),
            ValueCards = BuildValueCards(),
            ScriptSteps = BuildScriptSteps(),
            ExecutivePhrases =
            [
                "O GED transforma o acervo documental em informação rastreável.",
                "O OCR permite localizar conteúdo dentro dos documentos.",
                "A auditoria garante segurança e responsabilização.",
                "A inteligência hospitalar permite enxergar padrões a partir da documentação."
            ]
        };

        vm.ReadinessAlerts = BuildReadinessAlerts(vm.Overview);

        await _auditWriter.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "EXECUTIVE_DEMO", null, "Visualização do modo de demonstração executiva", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { action = "VIEW_EXECUTIVE_DEMO" }, ct);

        return View(vm);
    }

    private async Task<ExecutiveOverviewMetrics> LoadOverviewAsync(CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var sql = """
            select
                (select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A') as TotalDocuments,
                (select count(distinct s.document_id)::int from ged.document_search s where s.tenant_id=@tenantId and coalesce(s.ocr_text,'')<>'') as DocumentsWithOcr,
                (select count(*)::int from ged.ocr_job j where j.tenant_id=@tenantId and j.status='PENDING'::ged.ocr_status_enum) as DocumentsPendingOcr,
                (select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A' and (d.document_type_id is null)) as UnclassifiedDocuments,
                (select count(*)::int from ged.document_move_history m where m.tenant_id=@tenantId) as MovedDocuments,
                (select count(*)::int from ged.loan_request l where l.tenant_id=@tenantId) as DocumentRequests,
                (select count(*)::int from ged.user_tenant ut where ut.tenant_id=@tenantId and ut.is_active=true) as ActiveUsers
            """;

        return await conn.QuerySingleAsync<ExecutiveOverviewMetrics>(new CommandDefinition(sql, new { tenantId = _currentUser.TenantId }, cancellationToken: ct));
    }

    private static IReadOnlyList<ExecutiveReadinessAlert> BuildReadinessAlerts(ExecutiveOverviewMetrics metrics)
    {
        var alerts = new List<ExecutiveReadinessAlert>();
        if (metrics.DocumentsPendingOcr > 150)
            alerts.Add(new ExecutiveReadinessAlert { Title = "Fila OCR muito grande", Message = "Alto volume pendente pode atrasar a demonstração ao vivo.", Severity = "warning" });
        if (metrics.DocumentsPendingOcr > 0)
            alerts.Add(new ExecutiveReadinessAlert { Title = "Documentos sem OCR", Message = "Há documentos sem extração de texto concluída para busca avançada.", Severity = "info" });
        if (metrics.UnclassifiedDocuments > 50)
            alerts.Add(new ExecutiveReadinessAlert { Title = "Classificação pendente", Message = "Há muitos documentos sem classificação final.", Severity = "warning" });
        alerts.Add(new ExecutiveReadinessAlert { Title = "OCR com muitos erros", Message = "Validar amostra de OCR antes da reunião para evitar inconsistências na busca.", Severity = "info" });
        alerts.Add(new ExecutiveReadinessAlert { Title = "Upload lento", Message = "Use arquivos de demonstração menores para garantir fluidez na apresentação.", Severity = "info" });
        alerts.Add(new ExecutiveReadinessAlert { Title = "Logs críticos recentes", Message = "Revisar dashboard de auditoria e logs críticos antes da diretoria.", Severity = "warning" });
        return alerts;
    }

    private static IReadOnlyList<ExecutiveValueCard> BuildValueCards() =>
    [
        new() { Title = "Redução de tempo de localização documental", Description = "Localização de prontuários e anexos em segundos.", Icon = "bi-stopwatch" },
        new() { Title = "Rastreabilidade de documentos", Description = "Histórico completo de acesso, movimentação e uso.", Icon = "bi-diagram-3" },
        new() { Title = "Controle de acesso e sigilo", Description = "Perfis e permissões por área assistencial e administrativa.", Icon = "bi-shield-lock" },
        new() { Title = "Apoio à auditoria", Description = "Evidências confiáveis para conformidade e investigação.", Icon = "bi-clipboard2-check" },
        new() { Title = "Apoio à gestão hospitalar", Description = "Indicadores para decisões táticas e estratégicas.", Icon = "bi-bar-chart-line" },
        new() { Title = "Base para inteligência por OCR", Description = "Transformação de imagem em dado pesquisável e analisável.", Icon = "bi-cpu" }
    ];

    private static IReadOnlyList<ExecutiveScriptStep> BuildScriptSteps() =>
    [
        new() { Number = 1, Title = "Acesso e segurança", ExecutiveExplanation = "Demonstra autenticação e governança de perfis críticos.", PracticalBenefit = "Garante sigilo de dados hospitalares sensíveis.", ModuleUrl = "/Security" },
        new() { Number = 2, Title = "Organização por pastas", ExecutiveExplanation = "Navegação hierárquica do acervo institucional.", PracticalBenefit = "Padroniza organização documental entre setores.", ModuleUrl = "/Ged" },
        new() { Number = 3, Title = "Upload em lote", ExecutiveExplanation = "Importação de grandes volumes com produtividade.", PracticalBenefit = "Reduz esforço operacional no arquivo hospitalar.", ModuleUrl = "/Batches/New" },
        new() { Number = 4, Title = "OCR e pré-visualização", ExecutiveExplanation = "Conversão de imagem em texto pesquisável com visualização imediata.", PracticalBenefit = "Acelera leitura e conferência documental.", ModuleUrl = "/Ged/Processing" },
        new() { Number = 5, Title = "Busca de documentos hospitalares", ExecutiveExplanation = "Pesquisa textual e por metadados em todo o acervo.", PracticalBenefit = "Suporte ágil às áreas assistenciais e administrativas.", ModuleUrl = "/HospitalDocuments" },
        new() { Number = 6, Title = "Movimentação entre pastas", ExecutiveExplanation = "Controle formal de transferência entre unidades.", PracticalBenefit = "Evita perda de contexto e reduz riscos operacionais.", ModuleUrl = "/Ged" },
        new() { Number = 7, Title = "Solicitações/Loans", ExecutiveExplanation = "Fluxo de solicitação, aprovação e devolução de documentos.", PracticalBenefit = "Melhora SLA de atendimento interno.", ModuleUrl = "/Loans" },
        new() { Number = 8, Title = "Dashboard", ExecutiveExplanation = "Painel executivo com métricas de operação documental.", PracticalBenefit = "Visão rápida para tomada de decisão.", ModuleUrl = "/GedDashboard" },
        new() { Number = 9, Title = "Logs e auditoria", ExecutiveExplanation = "Registro de ações para rastreabilidade e conformidade.", PracticalBenefit = "Fortalece segurança e responsabilização.", ModuleUrl = "/Audit" },
        new() { Number = 10, Title = "Inteligência Hospitalar", ExecutiveExplanation = "Leitura estratégica de padrões extraídos dos documentos.", PracticalBenefit = "Apoia decisões clínicas e de gestão.", ModuleUrl = "/HospitalIntelligence" }
    ];
}
