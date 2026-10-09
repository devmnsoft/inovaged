using System.Text.Json;
using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Application.Retention;
using InovaGed.Application.Ged.Instruments;
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
    private readonly RetentionRecalcService _retention;
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentBulkClassificationService _bulkClassification;
    private readonly IClassificationPlanQueries _planQueries;

    public ClassificationController(
        ILogger<ClassificationController> logger,
        ICurrentUser currentUser,
        IDocumentClassificationQueries queries,
        IDocumentClassificationCommands commands,
        IDocumentClassificationAuditQueries auditQueries,
        RetentionRecalcService retention,
        IDbConnectionFactory db,
        IDocumentBulkClassificationService bulkClassification,
        IClassificationPlanQueries planQueries)
    {
        _logger = logger;
        _currentUser = currentUser;
        _queries = queries;
        _commands = commands;
        _auditQueries = auditQueries;
        _retention = retention;
        _db = db;
        _bulkClassification = bulkClassification;
        _planQueries = planQueries;
    }

    [HttpGet("")]
    public IActionResult Index(Guid? documentId = null)
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

    [HttpGet("Plans")]
    public IActionResult Plans() => RedirectToAction("Index", "ClassificationPlan");

    [HttpGet("Types")]
    public IActionResult Types() => RedirectToAction("Index", "ClassificationPlan", new { focus = "types" });

    [HttpGet("Rules")]
    public IActionResult Rules() => RedirectToAction("Index", "ClassificationPlan");

    [HttpGet("Panel")]
    public async Task<IActionResult> Panel(Guid documentId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return Unauthorized();

            if (documentId == Guid.Empty)
                return BadRequest("Documento inválido.");

            var tenantId = _currentUser.TenantId;

            var classification = await _queries.GetAsync(
                tenantId,
                documentId,
                ct);

            ViewData["DocumentId"] = documentId;

            return PartialView("_DocumentClassificationPanel", classification);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro em Classification/Panel. DocumentId={DocumentId}",
                documentId);

            return StatusCode(500, "Erro ao carregar painel de classificação.");
        }
    }

    [HttpGet("EditModal")]
    public async Task<IActionResult> EditModal(Guid documentId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return Unauthorized();

            if (documentId == Guid.Empty)
                return BadRequest("Documento inválido.");

            var tenantId = _currentUser.TenantId;

            var classification = await _queries.GetAsync(
                tenantId,
                documentId,
                ct);

            var types = await _queries.ListTypesAsync(
                tenantId,
                ct);

            var planItems = await _planQueries.ListAsync(
                tenantId,
                ct);

            await using var conn = await _db.OpenAsync(ct);
            var currentClassification = await conn.QueryFirstOrDefaultAsync<(Guid? ClassificationId, string? Code, string? Name)>(
                new CommandDefinition("""
                SELECT 
                    d.classification_id AS "ClassificationId",
                    COALESCE(pvi.code, cp.code) AS "Code",
                    COALESCE(pvi.name, cp.name) AS "Name"
                FROM ged.document d
                LEFT JOIN ged.classification_plan cp 
                    ON cp.tenant_id = d.tenant_id AND cp.id = d.classification_id
                LEFT JOIN ged.classification_plan_version_item pvi 
                    ON pvi.tenant_id = d.tenant_id AND pvi.classification_id = d.classification_id AND COALESCE(pvi.is_active, true)
                WHERE d.tenant_id = @tenantId AND d.id = @documentId
                LIMIT 1
                """, new { tenantId, documentId }, cancellationToken: ct));

            var vm = new EditClassificationVM
            {
                DocumentId = documentId,
                ClassificationId = currentClassification.ClassificationId,
                ClassificationLabel = currentClassification.ClassificationId.HasValue
                    ? (!string.IsNullOrWhiteSpace(currentClassification.Code)
                        ? $"{currentClassification.Code} — {currentClassification.Name}"
                        : currentClassification.Name)
                    : null,
                DocumentTypeId = classification?.DocumentTypeId,
                TagsCsv = classification?.Tags is { Count: > 0 }
                    ? string.Join(", ", classification.Tags)
                    : "",
                MetadataLines = classification?.Metadata is { Count: > 0 }
                    ? string.Join(
                        Environment.NewLine,
                        classification.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))
                    : ""
            };

            vm.AvailableClassifications = (planItems ?? Array.Empty<ClassificationPlanRow>())
                .Where(c => c.IsActive)
                .Select(c => new EditClassificationVM.ClassificationPlanItem
                {
                    Id = c.Id,
                    Code = c.Code,
                    Name = c.Name
                })
                .ToList();

            vm.AvailableTypes = (types ?? Array.Empty<DocumentTypeRowDto>())
                .Select(t => new EditClassificationVM.DocumentTypeItem
                {
                    Id = t.Id,
                    Name = t.Name
                })
                .ToList();

            return PartialView("_EditClassificationModal", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro em Classification/EditModal. DocumentId={DocumentId}",
                documentId);

            return StatusCode(500, "Erro ao abrir modal de edição.");
        }
    }

    [HttpPost("SaveManual")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveManual(
        [FromForm] Guid documentId,
        [FromForm] Guid? classificationId,
        [FromForm] Guid? documentTypeId,
        [FromForm] string? tagsCsv,
        [FromForm] string? metadataJson,
        [FromForm] string? metadataLines,
        CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                if (IsAjaxRequest())
                    return Unauthorized();

                return RedirectToAction("Login", "Account");
            }

            if (documentId == Guid.Empty)
            {
                if (IsAjaxRequest())
                    return BadRequest("Documento inválido.");

                TempData["Error"] = "Documento inválido.";
                return RedirectToAction("Index", "Ged");
            }

            if ((!classificationId.HasValue || classificationId.Value == Guid.Empty) &&
                (!documentTypeId.HasValue || documentTypeId.Value == Guid.Empty))
            {
                if (IsAjaxRequest())
                    return BadRequest("Informe a classificação arquivística ou o tipo documental.");

                TempData["Error"] = "Informe a classificação arquivística ou o tipo documental.";
                return RedirectToAction("Details", "Ged", new
                {
                    id = documentId,
                    openClassify = true
                });
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var tags = ParseTags(tagsCsv);

            var metadata = !string.IsNullOrWhiteSpace(metadataLines)
                ? ParseMetadataLines(metadataLines)
                : ParseMetadataJson(metadataJson);

            // 1. Aplica classificação arquivística (PCD) se fornecida
            if (classificationId.HasValue && classificationId.Value != Guid.Empty)
            {
                var bulkResult = await _bulkClassification.ApplyAsync(
                    tenantId,
                    userId,
                    new[] { documentId },
                    classificationId.Value,
                    ct);

                if (bulkResult.Failed > 0 || bulkResult.Denied > 0)
                {
                    var item = bulkResult.Items.FirstOrDefault(x => !x.Success);
                    var msg = item?.Message ?? "Erro ao aplicar classificação arquivística.";
                    if (IsAjaxRequest())
                        return BadRequest(msg);

                    TempData["Error"] = msg;
                    return RedirectToAction("Details", "Ged", new
                    {
                        id = documentId,
                        openClassify = true
                    });
                }
            }

            // 2. Aplica tipo documental, tags e metadados se fornecidos
            if (documentTypeId.HasValue && documentTypeId.Value != Guid.Empty)
            {
                await _commands.SaveManualAsync(
                    tenantId: tenantId,
                    documentId: documentId,
                    documentTypeId: documentTypeId,
                    userId: userId,
                    tags: tags,
                    metadata: metadata,
                    ct: ct);
            }
            else if ((tags != null && tags.Count > 0) || (metadata != null && metadata.Count > 0))
            {
                await _commands.SaveManualAsync(
                    tenantId: tenantId,
                    documentId: documentId,
                    documentTypeId: null,
                    userId: userId,
                    tags: tags,
                    metadata: metadata,
                    ct: ct);
            }

            var retentionResult = await RecalculateRetentionSafeAsync(
                tenantId,
                documentId,
                "MANUAL_SAVE",
                ct);

            var message = retentionResult.Success
                ? "Classificação salva e temporalidade recalculada."
                : "Classificação salva, mas a temporalidade não foi recalculada automaticamente. Verifique os logs.";

            if (IsAjaxRequest())
            {
                return Ok(new
                {
                    success = true,
                    retentionRecalculated = retentionResult.Success,
                    message
                });
            }

            TempData["Success"] = message;

            return RedirectToAction("Details", "Ged", new
            {
                id = documentId,
                openClassify = true
            });
        }
        catch (JsonException jex)
        {
            _logger.LogWarning(
                jex,
                "JSON inválido em SaveManual. DocumentId={DocumentId}",
                documentId);

            if (IsAjaxRequest())
                return BadRequest("Metadata JSON inválido.");

            TempData["Error"] = "Metadata JSON inválido.";

            return RedirectToAction("Details", "Ged", new
            {
                id = documentId,
                openClassify = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro em Classification/SaveManual. DocumentId={DocumentId}",
                documentId);

            if (IsAjaxRequest())
                return StatusCode(500, "Erro ao salvar classificação.");

            TempData["Error"] = "Erro ao salvar classificação.";

            if (documentId != Guid.Empty)
            {
                return RedirectToAction("Details", "Ged", new
                {
                    id = documentId,
                    openClassify = true
                });
            }

            return RedirectToAction("Index", "Ged");
        }
    }

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
            if (!_currentUser.IsAuthenticated)
            {
                if (IsAjaxRequest())
                    return Unauthorized();

                return RedirectToAction("Login", "Account");
            }

            if (documentId == Guid.Empty)
            {
                if (IsAjaxRequest())
                    return BadRequest("Documento inválido.");

                TempData["Error"] = "Documento inválido.";
                return RedirectToAction("Index", "Ged");
            }

            if (suggestedTypeId == Guid.Empty)
            {
                if (IsAjaxRequest())
                    return BadRequest("Sugestão inválida.");

                TempData["Error"] = "Sugestão inválida.";
                return RedirectToAction("Details", "Ged", new
                {
                    id = documentId,
                    openClassify = true
                });
            }

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

            var retentionResult = await RecalculateRetentionSafeAsync(
                tenantId,
                documentId,
                "APPLY_SUGGESTION",
                ct);

            var message = retentionResult.Success
                ? "Sugestão aplicada e temporalidade recalculada."
                : "Sugestão aplicada, mas a temporalidade não foi recalculada automaticamente. Verifique os logs.";

            if (IsAjaxRequest())
            {
                return Ok(new
                {
                    success = true,
                    retentionRecalculated = retentionResult.Success,
                    message
                });
            }

            TempData["Success"] = message;

            return RedirectToAction("Details", "Ged", new
            {
                id = documentId,
                openClassify = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro em Classification/ApplySuggestion. DocumentId={DocumentId}",
                documentId);

            if (IsAjaxRequest())
                return StatusCode(500, "Erro ao aplicar sugestão.");

            TempData["Error"] = "Erro ao aplicar sugestão.";

            if (documentId != Guid.Empty)
            {
                return RedirectToAction("Details", "Ged", new
                {
                    id = documentId,
                    openClassify = true
                });
            }

            return RedirectToAction("Index", "Ged");
        }
    }

    [HttpGet("Audit")]
    public async Task<IActionResult> Audit(
        Guid documentId,
        int take = 30,
        CancellationToken ct = default)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return Unauthorized();

            if (documentId == Guid.Empty)
                return BadRequest("Documento inválido.");

            var tenantId = _currentUser.TenantId;

            var rows = await _auditQueries.ListByDocumentAsync(
                tenantId,
                documentId,
                take,
                ct);

            ViewData["DocumentId"] = documentId;

            return PartialView("_DocumentClassificationAuditTimeline", rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro em Classification/Audit. DocumentId={DocumentId}",
                documentId);

            return StatusCode(500, "Erro ao carregar histórico de classificação.");
        }
    }

    private async Task<RetentionRecalcResult> RecalculateRetentionSafeAsync(
        Guid tenantId,
        Guid documentId,
        string origin,
        CancellationToken ct)
    {
        try
        {
            await _retention.RunOneAsync(
                tenantId,
                documentId,
                dueSoonDays: 30,
                ct);

            _logger.LogInformation(
                "Temporalidade recalculada após classificação. Tenant={TenantId}, DocumentId={DocumentId}, Origin={Origin}",
                tenantId,
                documentId,
                origin);

            return RetentionRecalcResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Classificação salva, mas falhou o recálculo da temporalidade. Tenant={TenantId}, DocumentId={DocumentId}, Origin={Origin}",
                tenantId,
                documentId,
                origin);

            return RetentionRecalcResult.Fail(ex.Message);
        }
    }

    private bool IsAjaxRequest()
    {
        return string.Equals(
                   Request.Headers["X-Requested-With"],
                   "XMLHttpRequest",
                   StringComparison.OrdinalIgnoreCase)
               || (Request.Headers.TryGetValue("Accept", out var accept)
                   && accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ParseTags(string? tagsCsv)
    {
        if (string.IsNullOrWhiteSpace(tagsCsv))
            return Array.Empty<string>();

        return tagsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> ParseMetadataJson(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);

        return dict ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> ParseMetadataLines(string? lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(lines))
            return dict;

        foreach (var raw in lines.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = raw.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var idx = line.IndexOf('=');

            if (idx <= 0)
                continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(key))
                dict[key] = value;
        }

        return dict;
    }

    private sealed class RetentionRecalcResult
    {
        public bool Success { get; private init; }

        public string? Error { get; private init; }

        public static RetentionRecalcResult Ok()
        {
            return new RetentionRecalcResult
            {
                Success = true
            };
        }

        public static RetentionRecalcResult Fail(string? error)
        {
            return new RetentionRecalcResult
            {
                Success = false,
                Error = error
            };
        }
    }
}
