using System.Text.Json;
using InovaGed.Application.Classification;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.Classification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Classification")]
public sealed class ClassificationController : Controller
{
    private readonly ILogger<ClassificationController> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentClassificationQueries _queries;
    private readonly IDocumentClassificationCommands _commands;
    private readonly IDocumentClassificationAuditQueries _auditQueries;

    public ClassificationController(
        ILogger<ClassificationController> logger,
        ICurrentUser currentUser,
        IDocumentClassificationQueries queries,
        IDocumentClassificationCommands commands,
        IDocumentClassificationAuditQueries auditQueries)
    {
        _logger = logger;
        _currentUser = currentUser;
        _queries = queries;
        _commands = commands;
        _auditQueries = auditQueries;
    }

    // ✅ /Classification
    // ✅ /Classification?documentId=GUID -> redireciona pro GED/Details com openClassify=true
    [HttpGet("")]
    public IActionResult Index(Guid? documentId = null)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            if (documentId.HasValue && documentId.Value != Guid.Empty)
            {
                return RedirectToAction("Details", "Ged", new
                {
                    id = documentId.Value,
                    openClassify = true
                });
            }

            return RedirectToAction("Index", "Ged");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Classification/Index. documentId={DocumentId}", documentId);
            TempData["Error"] = "Erro ao abrir classificação.";
            return RedirectToAction("Index", "Ged");
        }
    }

    // GET /Classification/Panel?documentId=GUID
    [HttpGet("Panel")]
    public async Task<IActionResult> Panel(Guid documentId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var cls = await _queries.GetAsync(tenantId, documentId, ct);

            ViewData["DocumentId"] = documentId;
            return PartialView("_DocumentClassificationPanel", cls);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Classification/Panel. DocumentId={DocumentId}", documentId);
            return StatusCode(500, "Erro ao carregar painel de classificação.");
        }
    }

    // GET /Classification/EditModal?documentId=GUID
    [HttpGet("EditModal")]
    public async Task<IActionResult> EditModal(Guid documentId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var cls = await _queries.GetAsync(tenantId, documentId, ct);
            var types = await _queries.ListTypesAsync(tenantId, ct);

            var vm = new EditClassificationVM
            {
                DocumentId = documentId,
                DocumentTypeId = cls?.DocumentTypeId,
                TagsCsv = (cls?.Tags is { Count: > 0 }) ? string.Join(", ", cls.Tags) : "",
                MetadataLines = (cls?.Metadata is { Count: > 0 })
                    ? string.Join(Environment.NewLine, cls.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))
                    : ""
            };

            vm.AvailableTypes = (types ?? Array.Empty<DocumentTypeRowDto>())
                .Select(t => new EditClassificationVM.DocumentTypeItem { Id = t.Id, Name = t.Name })
                .ToList();

            return PartialView("_EditClassificationModal", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Classification/EditModal. DocumentId={DocumentId}", documentId);
            return StatusCode(500, "Erro ao abrir modal de edição.");
        }
    }

    // POST /Classification/SaveManual
    [HttpPost("SaveManual")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveManual(
        [FromForm] Guid documentId,
        [FromForm] Guid? documentTypeId,
        [FromForm] string? tagsCsv,
        [FromForm] string? metadataJson,
        CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var tags = ParseTags(tagsCsv);
            var metadata = ParseMetadata(metadataJson);

            await _commands.SaveManualAsync(
                tenantId: tenantId,
                documentId: documentId,
                documentTypeId: documentTypeId,
                userId: userId,
                tags: tags,
                metadata: metadata,
                ct: ct);

            return Ok(new { success = true });
        }
        catch (JsonException jex)
        {
            _logger.LogWarning(jex, "JSON inválido em SaveManual. DocumentId={DocumentId}", documentId);
            return BadRequest("Metadata JSON inválido.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Classification/SaveManual. DocumentId={DocumentId}", documentId);
            return StatusCode(500, "Erro ao salvar classificação.");
        }
    }

    // POST /Classification/ApplySuggestion
    [HttpPost("ApplySuggestion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplySuggestion(
        [FromForm] Guid documentId,
        [FromForm] Guid suggestedTypeId,
        [FromForm] decimal? suggestedConfidence,
        [FromForm] string? suggestedSummary,
        CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            await _commands.ApplySuggestionAsync(
                tenantId: tenantId,
                documentId: documentId,
                suggestedTypeId: suggestedTypeId,
                suggestedConfidence: suggestedConfidence,
                suggestedSummary: suggestedSummary,
                userId: userId,
                ct: ct);

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Classification/ApplySuggestion. DocumentId={DocumentId}", documentId);
            return StatusCode(500, "Erro ao aplicar sugestão.");
        }
    }

    // =========================
    // Helpers
    // =========================
    private static IReadOnlyList<string> ParseTags(string? tagsCsv)
    {
        if (string.IsNullOrWhiteSpace(tagsCsv))
            return Array.Empty<string>();

        return tagsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
        return dict ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }


    [HttpGet("Audit")]
    public async Task<IActionResult> Audit(Guid documentId, int take = 20, CancellationToken ct = default)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var rows = await _auditQueries.ListByDocumentAsync(tenantId, documentId, take, ct);

            ViewData["DocumentId"] = documentId;
            return PartialView("_DocumentClassificationAuditTimeline", rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Classification/Audit. DocumentId={DocumentId}", documentId);
            return StatusCode(500, "Erro ao carregar histórico de classificação.");
        }
    }
}
