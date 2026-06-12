using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using InovaGed.Application.SmartSearch;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.HospitalDocumentsAccess)]
public sealed class SmartSearchController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly ISmartSearchService _smartSearch;
    private readonly IDocumentChatService _documentChat;
    private readonly ISearchStatisticsService _statistics;
    private readonly ISmartSearchRepository _repository;
    private readonly IAuditWriter _audit;
    private readonly ILogger<SmartSearchController> _logger;

    public SmartSearchController(ICurrentUser currentUser, ISmartSearchService smartSearch, IDocumentChatService documentChat, ISearchStatisticsService statistics, ISmartSearchRepository repository, IAuditWriter audit, ILogger<SmartSearchController> logger)
    {
        _currentUser = currentUser;
        _smartSearch = smartSearch;
        _documentChat = documentChat;
        _statistics = statistics;
        _repository = repository;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (!_currentUser.IsAuthenticated) return RedirectToAction("Login", "Account");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Search([FromForm] SmartSearchRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, message = "Sessão expirada." });
        request.TenantId = _currentUser.TenantId;
        request.UserId = _currentUser.UserId;
        request.IsAdmin = RolePolicyHelper.IsFullAdmin(User);
        request.Page = request.Page <= 0 ? 1 : request.Page;
        request.PageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        try
        {
            var result = await _smartSearch.SearchAsync(request, ct);
            await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "SEARCH_SMART_QUERY", null, "Busca inteligente executada", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { queryHashOnly = true, result.Total, request.Page, request.PageSize, correlationId = HttpContext.TraceIdentifier }, ct);
            if (result.Intent.ClinicalTerms.Count > 0)
                await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "SEARCH_SENSITIVE_TERM", null, "Busca inteligente com termo sensível registrada de forma reduzida", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { termsCount = result.Intent.ClinicalTerms.Count, correlationId = HttpContext.TraceIdentifier }, ct);
            return Json(new { success = true, result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na busca inteligente. CorrelationId={CorrelationId}", HttpContext.TraceIdentifier);
            return StatusCode(500, new { success = false, message = "Não foi possível executar a busca inteligente agora.", correlationId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Suggest([FromQuery] string? q, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, items = Array.Empty<object>() });
        var items = await _smartSearch.SuggestAsync(_currentUser.TenantId, q, ct);
        return Json(new { success = true, items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExplainResult([FromForm] Guid documentId, CancellationToken ct)
    {
        await _repository.LogAccessAsync(_currentUser.TenantId, _currentUser.UserId, documentId, "SMART_SEARCH", "SEARCH_DOCUMENT_OPENED", ct);
        return Json(new { success = true, message = "Os motivos são calculados pela combinação de nome, período, idade, OCR, tipo documental e similaridade textual." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AskDocument([FromForm] DocumentQuestionRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false });
        var answer = await _documentChat.AskAsync(_currentUser.TenantId, _currentUser.UserId, request, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "SEARCH_DOCUMENT_QUESTION", request.DocumentId, "Pergunta documental respondida com base no OCR", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { answer.FoundInDocument, evidenceCount = answer.EvidenceSnippets.Count, correlationId = HttpContext.TraceIdentifier }, ct);
        return Json(new { success = true, answer });
    }

    [HttpGet]
    public async Task<IActionResult> Statistics(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return RedirectToAction("Login", "Account");
        var model = await _statistics.GetAsync(_currentUser.TenantId, ct);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reindex([FromForm] Guid? documentId, CancellationToken ct)
    {
        if (!RolePolicyHelper.IsFullAdmin(User) && !User.IsInNormalizedRole(AppRoles.Administrador)) return Forbid();
        var count = await _repository.ReindexAsync(_currentUser.TenantId, documentId, ct);
        return Json(new { success = true, count });
    }
}
