using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Application.Ged.Search;
using InovaGed.Application.Security;
using InovaGed.Application.SmartSearch;
using SmartSearchRequestDto = InovaGed.Application.SmartSearch.SmartSearchRequest;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.HospitalDocumentsAccess)]
[TypeFilter(typeof(SmartSearchExceptionFilter))]
public sealed class SmartSearchController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly ISmartSearchService _smartSearch;
    private readonly IDocumentChatService _documentChat;
    private readonly IDocumentAssistantService _documentAssistant;
    private readonly ISearchStatisticsService _statistics;
    private readonly ISmartSearchRepository _repository;
    private readonly IGedSmartSearchDiagnosticsService _diagnostics;
    private readonly IAuditWriter _audit;
    private readonly ILogger<SmartSearchController> _logger;

    public SmartSearchController(ICurrentUser currentUser, ISmartSearchService smartSearch, IDocumentChatService documentChat, IDocumentAssistantService documentAssistant, ISearchStatisticsService statistics, ISmartSearchRepository repository, IGedSmartSearchDiagnosticsService diagnostics, IAuditWriter audit, ILogger<SmartSearchController> logger)
    {
        _currentUser = currentUser;
        _smartSearch = smartSearch;
        _documentChat = documentChat;
        _documentAssistant = documentAssistant;
        _statistics = statistics;
        _repository = repository;
        _diagnostics = diagnostics;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    [ActionName("Ask")]
    public IActionResult AskPage([FromQuery] string? q) => Index(q);

    [HttpGet]
    public IActionResult History()
    {
        ViewBag.ShowHistory = true;
        return View("Index");
    }

    [HttpGet]
    public IActionResult Insights() => RedirectToAction(nameof(Statistics));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromForm] string question, [FromForm] string? conversationId, [FromForm] int page = 1, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, message = "Sua sessão expirou. Entre novamente." });
        try
        {
            var response = await _documentAssistant.AskAsync(new DocumentAssistantQuery
            {
                TenantId = _currentUser.TenantId,
                UserId = _currentUser.UserId,
                IsAdmin = RolePolicyHelper.IsFullAdmin(User),
                Question = question,
                Page = page,
                ConversationId = conversationId,
                SecurityContext = new DocumentAssistantSecurityContext
                {
                    TenantId = _currentUser.TenantId,
                    UserId = _currentUser.UserId,
                    CanReadOcr = User.IsInRole(AppRoles.Administrador) || User.HasClaim("permission", "GED.OCR.READ"),
                    CanViewRestrictedDocuments = RolePolicyHelper.IsFullAdmin(User)
                }
            }, ct);
            await _repository.SaveConversationTurnAsync(_currentUser.TenantId, _currentUser.UserId, response.ConversationId, question.Trim(), response, ct);
            await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "DOCUMENT_ASSISTANT_QUERY", null, "Consulta ao assistente documental", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { response.Total, response.Page, correlationId = HttpContext.TraceIdentifier }, ct);
            if (response.AppliedCriteria.IsSensitive)
                await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "DOCUMENT_ASSISTANT_SENSITIVE_QUERY", null, "Consulta sensível no assistente registrada sem armazenar o conteúdo", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { response.Total, correlationId = HttpContext.TraceIdentifier }, ct);
            return Json(new { success = true, response });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new EmptyResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no assistente documental. CorrelationId={CorrelationId}", HttpContext.TraceIdentifier);
            return StatusCode(500, new { success = false, message = "Não foi possível consultar os documentos agora.", correlationId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet]
    public async Task<IActionResult> HistoryData(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, items = Array.Empty<object>() });
        var items = await _repository.GetConversationHistoryAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        return Json(new { success = true, items });
    }

    [HttpGet]
    public async Task<IActionResult> Conversation(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, items = Array.Empty<object>() });
        if (id == Guid.Empty) return BadRequest(new { success = false, message = "Conversa inválida." });
        try
        {
            var items = await _repository.GetConversationMessagesAsync(_currentUser.TenantId, _currentUser.UserId, id, ct);
            return Json(new { success = true, items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao carregar conversa do SmartSearch. Conversation={ConversationId} CorrelationId={CorrelationId}", id, HttpContext.TraceIdentifier);
            return StatusCode(503, new { success = false, items = Array.Empty<object>(), message = "Não foi possível carregar esta conversa agora.", correlationId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet]
    public async Task<IActionResult> SavedSearches(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, message = "Sua sessão expirou." });
        try
        {
            var items = await _repository.GetSavedSearchesAsync(_currentUser.TenantId, _currentUser.UserId, ct);
            if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)) return Json(new { success = true, items });
            return View(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao carregar buscas salvas. Tenant={TenantId} User={UserId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, HttpContext.TraceIdentifier);
            if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return StatusCode(503, new { success = false, items = Array.Empty<object>(), message = "Suas buscas salvas estão temporariamente indisponíveis.", correlationId = HttpContext.TraceIdentifier });
            TempData["Error"] = "Não foi possível carregar suas buscas salvas agora. Tente novamente em instantes.";
            return View(Array.Empty<SmartSearchSavedSearch>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSearch([FromForm] string query, [FromForm] string? name, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        name = string.IsNullOrWhiteSpace(name) ? query : name.Trim();
        if (query.Length is < 2 or > 500 || name.Length > 120) return BadRequest(new { success = false, message = "Busca inválida." });
        await _repository.SaveSearchAsync(_currentUser.TenantId, _currentUser.UserId, name, query, ct);
        await AuditSavedSearchAsync("CREATE", null, "Busca salva criada", new { name, queryLength = query.Length }, ct);
        return Json(new { success = true, message = "Busca salva com segurança na sua conta." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameSavedSearch([FromForm] Guid id, [FromForm] string name, CancellationToken ct)
    {
        name = (name ?? string.Empty).Trim();
        if (id == Guid.Empty || name.Length is < 1 or > 120) return BadRequest(new { success = false, message = "Informe um nome válido." });
        if (!await _repository.RenameSavedSearchAsync(_currentUser.TenantId, _currentUser.UserId, id, name, ct)) return NotFound(new { success = false, message = "Busca salva não encontrada." });
        await AuditSavedSearchAsync("UPDATE", id, "Busca salva renomeada", new { name }, ct);
        return Json(new { success = true, message = "Nome atualizado." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FavoriteSavedSearch([FromForm] Guid id, [FromForm] bool isFavorite, CancellationToken ct)
    {
        if (id == Guid.Empty) return BadRequest(new { success = false });
        if (!await _repository.SetSavedSearchFavoriteAsync(_currentUser.TenantId, _currentUser.UserId, id, isFavorite, ct)) return NotFound(new { success = false });
        await AuditSavedSearchAsync("UPDATE", id, isFavorite ? "Busca salva favoritada" : "Busca salva removida dos favoritos", new { isFavorite }, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunSavedSearch([FromForm] Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) return BadRequest(new { success = false });
        var query = await _repository.RunSavedSearchAsync(_currentUser.TenantId, _currentUser.UserId, id, ct);
        if (query is null) return NotFound(new { success = false, message = "Busca salva não encontrada." });
        await AuditSavedSearchAsync("VIEW", id, "Busca salva executada", new { queryLength = query.Length }, ct);
        return Json(new { success = true, query });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSavedSearch([FromForm] Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) return BadRequest();
        if (!await _repository.DeleteSavedSearchAsync(_currentUser.TenantId, _currentUser.UserId, id, ct))
            return NotFound(new { success = false, message = "Busca salva não encontrada." });
        await AuditSavedSearchAsync("DELETE", id, "Busca salva excluída", null, ct);
        return Json(new { success = true });
    }

    private Task AuditSavedSearchAsync(string action, Guid? id, string message, object? details, CancellationToken ct)
        => _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, action, "SMART_SEARCH_SAVED_SEARCH", id, message,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), details, ct);

    [HttpGet]
    public IActionResult Index([FromQuery] string? q)
    {
        if (!_currentUser.IsAuthenticated) return RedirectToAction("Login", "Account");
        ViewBag.InitialQuestion = string.IsNullOrWhiteSpace(q) ? null : q.Trim()[..Math.Min(q.Trim().Length, 500)];
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Search([FromForm] SmartSearchRequestDto request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, message = "Sessão expirada." });
        request.TenantId = _currentUser.TenantId;
        request.UserId = _currentUser.UserId;
        request.IsAdmin = RolePolicyHelper.IsFullAdmin(User);
        request.Page = request.Page <= 0 ? 1 : request.Page;
        request.PageSize = request.PageSize <= 0 ? 20 : request.PageSize;
        if (!string.Equals(request.Source, "folder", StringComparison.OrdinalIgnoreCase)) request.FolderId = null;

        try
        {
            var result = await _smartSearch.SearchAsync(request, ct);
            await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "SEARCH_SMART_QUERY", null, "Busca inteligente executada", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { queryHashOnly = true, result.Total, request.Page, request.PageSize, correlationId = HttpContext.TraceIdentifier }, ct);
            if (result.Intent.ClinicalTerms.Count > 0)
                await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "SEARCH_SENSITIVE_TERM", null, "Busca inteligente com termo sensível registrada de forma reduzida", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { termsCount = result.Intent.ClinicalTerms.Count, correlationId = HttpContext.TraceIdentifier }, ct);
            return Json(new { success = true, result });
        }
        catch (ArgumentException ex) when (ex.Message.Contains("DateTime", StringComparison.OrdinalIgnoreCase))
        {
            var correlationId = HttpContext.TraceIdentifier;
            _logger.LogError(ex, "Erro de data na busca inteligente. CorrelationId={CorrelationId}", correlationId);

            return BadRequest(new
            {
                success = false,
                code = "INVALID_DATE_FILTER",
                message = "A busca recebeu um filtro de data inválido. Tente novamente ou remova o período informado.",
                correlationId
            });
        }
        catch (Exception ex) when (IsPostgresSchemaException(ex))
        {
            var correlationId = HttpContext.TraceIdentifier;
            _logger.LogError(ex, "Schema de busca inteligente incompleto. CorrelationId={CorrelationId}", correlationId);
            return StatusCode(503, new { success = false, code = "SEARCH_SCHEMA_NOT_READY", message = "Índice de busca não configurado. Execute migrations ou use busca ampla no GED.", correlationId });
        }
        catch (TimeoutException ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            _logger.LogError(ex, "Timeout na busca inteligente. CorrelationId={CorrelationId}", correlationId);
            return StatusCode(504, new { success = false, code = "SEARCH_TIMEOUT", message = "Busca demorou demais. Tente reduzir filtros.", correlationId });
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
    public async Task<IActionResult> Feedback([FromForm] Guid documentId, [FromForm] string conversationId, [FromForm] bool helpful, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, message = "Sessão expirada." });
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(conversationId) || conversationId.Length > 64 ||
            !conversationId.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            return BadRequest(new { success = false, message = "Feedback inválido." });

        await _repository.SaveFeedbackAsync(_currentUser.TenantId, _currentUser.UserId, documentId, conversationId, helpful, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPDATE", "SMART_SEARCH_FEEDBACK", documentId,
            helpful ? "Resultado marcado como útil" : "Resultado marcado como não útil", HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), new { helpful, correlationId = HttpContext.TraceIdentifier }, ct);
        return Json(new { success = true, message = "Obrigado. Seu feedback foi registrado." });
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

    [HttpGet]
    public Task<IActionResult> Metrics(CancellationToken ct) => Statistics(ct);

    [HttpGet]
    public async Task<IActionResult> Diagnostics(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return RedirectToAction("Login", "Account");
        if (!RolePolicyHelper.IsFullAdmin(User)) return Forbid();
        var model = await _diagnostics.GetAsync(_currentUser.TenantId, ct);
        if ((Request.Headers.Accept.ToString() ?? string.Empty).Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Json(new { success = true, model });
        return View(model);
    }

    [HttpGet]
    public Task<IActionResult> Settings(CancellationToken ct) => AdminPageAsync("Settings", ct);
    [HttpGet]
    public Task<IActionResult> Synonyms(CancellationToken ct) => AdminPageAsync("Synonyms", ct);
    [HttpGet]
    public Task<IActionResult> Intents(CancellationToken ct) => AdminPageAsync("Intents", ct);
    [HttpGet]
    public Task<IActionResult> Quality(CancellationToken ct) => AdminPageAsync("Quality", ct);
    [HttpGet]
    [ActionName("Feedback")]
    public Task<IActionResult> AdminFeedback(CancellationToken ct) => AdminPageAsync("Feedback", ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSynonym([FromForm] Guid? id, [FromForm] string term, [FromForm] string synonym,
        [FromForm] string category, [FromForm] decimal weight = 1, [FromForm] bool active = false, CancellationToken ct = default)
    {
        if (!RolePolicyHelper.IsFullAdmin(User)) return Forbid();
        term = (term ?? string.Empty).Trim(); synonym = (synonym ?? string.Empty).Trim(); category = (category ?? "business").Trim();
        if (term.Length is < 2 or > 120 || synonym.Length is < 2 or > 120 || category.Length > 40 || weight is < 0.1m or > 10m)
            ModelState.AddModelError(string.Empty, "Revise os termos e informe um peso entre 0,1 e 10.");
        if (!ModelState.IsValid) return await AdminPageAsync("Synonyms", ct);
        await _repository.SaveSynonymAsync(_currentUser.TenantId, id, term, synonym, category, weight, active, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPDATE", "SMART_SEARCH_SYNONYM", id, "Dicionário do SmartSearch atualizado", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { category, active }, ct);
        TempData["Success"] = "Sinônimo salvo e disponível para as próximas consultas.";
        return RedirectToAction(nameof(Synonyms));
    }

    private async Task<IActionResult> AdminPageAsync(string section, CancellationToken ct)
    {
        if (!RolePolicyHelper.IsFullAdmin(User)) return Forbid();
        var model = await _repository.GetAdminDashboardAsync(_currentUser.TenantId, section, ct);
        return View("Admin", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReindexMissing(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized(new { success = false, message = "Sessão expirada." });
        if (!RolePolicyHelper.IsFullAdmin(User) && !User.IsInNormalizedRole(AppRoles.Administrador)) return Forbid();
        try
        {
            var count = await _diagnostics.EnqueueReindexMissingAsync(_currentUser.TenantId, ct);
            return Json(new { success = true, jobsCreated = count, affected = count });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { success = false, code = "PROCESSING_JOB_SCHEMA_MISSING", message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reindex([FromForm] Guid? documentId, CancellationToken ct)
    {
        if (!RolePolicyHelper.IsFullAdmin(User) && !User.IsInNormalizedRole(AppRoles.Administrador)) return Forbid();
        var count = await _repository.ReindexAsync(_currentUser.TenantId, documentId, ct);
        return Json(new { success = true, count });
    }

    private static bool IsPostgresSchemaException(Exception ex)
    {
        if (ex.GetType().Name != "PostgresException") return false;
        var sqlState = ex.GetType().GetProperty("SqlState")?.GetValue(ex) as string;
        return sqlState is "42P01" or "42703";
    }
}
