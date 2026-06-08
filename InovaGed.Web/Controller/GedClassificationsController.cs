using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class GedClassificationsController : Controller
{
    private static readonly string[] AllowedRoles = [AppRoles.Admin, AppRoles.AdministradorOphir, AppRoles.ArquivistaOphir, AppRoles.Arquivista];
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentClassificationCommands _commands;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;
    private readonly ILogger<GedClassificationsController> _logger;

    public GedClassificationsController(
        IDbConnectionFactory db,
        IDocumentClassificationCommands commands,
        ICurrentUser currentUser,
        IAuditWriter audit,
        ILogger<GedClassificationsController> logger)
    {
        _db = db;
        _commands = commands;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("/Ged/Classifications/QuickList")]
    public async Task<IActionResult> QuickList([FromQuery] string? q, [FromQuery] Guid? documentId, CancellationToken ct)
    {
        await using var con = await _db.OpenAsync(ct);
        var rows = (await con.QueryAsync<QuickClassificationRow>(new CommandDefinition("""
SELECT id AS "Id", name AS "Name", description AS "Description"
FROM ged.document_type
WHERE tenant_id = @TenantId
  AND reg_status = 'A'
  AND (@Q IS NULL OR @Q = '' OR name ILIKE '%' || @Q || '%' OR code ILIKE '%' || @Q || '%')
ORDER BY lower(name)
LIMIT 50;
""", new { TenantId = _currentUser.TenantId, Q = q }, cancellationToken: ct))).ToList();

        Guid? suggestedId = null;
        if (documentId.HasValue && documentId.Value != Guid.Empty)
        {
            var ocrText = await GetCurrentVersionOcrTextAsync(con, _currentUser.TenantId, documentId.Value, ct);
            suggestedId = SuggestByOcrText(rows, ocrText);
        }

        return Json(new
        {
            success = true,
            items = rows.Select(x => new
            {
                id = x.Id,
                name = x.Name,
                description = x.Description,
                color = ColorFor(x.Name),
                icon = IconFor(x.Name),
                suggestedByOcr = suggestedId == x.Id
            })
        });
    }

    [HttpPost("/Ged/Documents/{id:guid}/Classification")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UpdateDocumentClassification(Guid id, [FromBody] UpdateClassificationRequest? request, CancellationToken ct)
    {
        if (!AllowedRoles.Any(User.IsInRole)) return Forbid();
        if (id == Guid.Empty) return BadRequest(new { success = false, message = "Documento inválido." });
        request ??= new UpdateClassificationRequest(null, "Classificação rápida pela listagem");

        await using var con = await _db.OpenAsync(ct);
        var oldClassificationId = await con.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
SELECT COALESCE(dc.document_type_id, d.type_id)
FROM ged.document d
LEFT JOIN LATERAL (
    SELECT document_type_id
    FROM ged.document_classification x
    WHERE x.tenant_id = d.tenant_id AND x.document_id = d.id AND x.reg_status = 'A'
    ORDER BY x.classified_at DESC NULLS LAST, x.created_at DESC NULLS LAST
    LIMIT 1
) dc ON true
WHERE d.tenant_id = @TenantId AND d.id = @DocumentId AND d.reg_status = 'A'
LIMIT 1;
""", new { TenantId = _currentUser.TenantId, DocumentId = id }, cancellationToken: ct));

        var newId = request.ClassificationId.HasValue && request.ClassificationId.Value != Guid.Empty
            ? request.ClassificationId
            : null;
        string? label = null;
        if (newId.HasValue)
        {
            label = await con.ExecuteScalarAsync<string?>(new CommandDefinition("""
SELECT name FROM ged.document_type
WHERE tenant_id = @TenantId AND id = @Id AND reg_status = 'A'
LIMIT 1;
""", new { TenantId = _currentUser.TenantId, Id = newId.Value }, cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(label)) return BadRequest(new { success = false, message = "Classificação não encontrada." });
        }

        await _commands.SaveManualAsync(_currentUser.TenantId, id, newId, _currentUser.UserId, Array.Empty<string>(), new Dictionary<string, string>(), ct);

        await _audit.WriteAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            "DOCUMENT_CLASSIFICATION_CHANGED",
            "DOCUMENT",
            id,
            "Classificação rápida alterada pela listagem GED.",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            new
            {
                documentId = id,
                oldClassificationId,
                newClassificationId = newId,
                reason = string.IsNullOrWhiteSpace(request.Reason) ? "Classificação rápida pela listagem" : request.Reason,
                correlationId = HttpContext.TraceIdentifier,
                timestampUtc = DateTime.UtcNow
            },
            ct);

        return Json(new
        {
            success = true,
            classificationId = newId,
            classificationLabel = label ?? "Classificar",
            classificationColor = newId.HasValue ? ColorFor(label) : null,
            classificationIcon = newId.HasValue ? IconFor(label) : "bi-tag",
            message = newId.HasValue ? "Documento classificado com sucesso." : "Classificação removida."
        });
    }

    private static async Task<string?> GetCurrentVersionOcrTextAsync(System.Data.IDbConnection con, Guid tenantId, Guid documentId, CancellationToken ct)
        => await con.ExecuteScalarAsync<string?>(new CommandDefinition("""
SELECT COALESCE(NULLIF(ds.ocr_text,''), NULLIF(v.ocr_text,''), NULLIF(v.content_text,''))
FROM ged.document d
LEFT JOIN ged.document_version v ON v.tenant_id = d.tenant_id AND v.id = d.current_version_id
LEFT JOIN ged.document_search ds ON ds.tenant_id = d.tenant_id AND ds.document_id = d.id AND ds.version_id = v.id
WHERE d.tenant_id = @TenantId AND d.id = @DocumentId AND d.reg_status = 'A'
LIMIT 1;
""", new { TenantId = tenantId, DocumentId = documentId }, cancellationToken: ct));

    private static Guid? SuggestByOcrText(IEnumerable<QuickClassificationRow> rows, string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return null;
        var text = ocrText.ToUpperInvariant();
        string? target = text switch
        {
            var x when x.Contains("APAC") => "APAC",
            var x when x.Contains("LAUDO") => "Laudo",
            var x when x.Contains("AGENDAMENTO") => "Agendamento",
            var x when x.Contains("PRESCRI") => "Prescrição",
            var x when x.Contains("EXAME") => "Exame",
            var x when x.Contains("AUTORIZA") => "Autorização",
            _ => null
        };
        return target is null ? null : rows.FirstOrDefault(r => string.Equals(r.Name, target, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static string ColorFor(string? name) => (name ?? string.Empty).ToUpperInvariant() switch
    {
        var x when x.Contains("APAC") => "#7c3aed",
        var x when x.Contains("LAUDO") => "#0f766e",
        var x when x.Contains("EXAME") => "#0369a1",
        var x when x.Contains("PRESCRI") => "#16a34a",
        var x when x.Contains("JUR") => "#9333ea",
        var x when x.Contains("FINANCE") || x.Contains("FATURA") => "#ca8a04",
        _ => "#2563eb"
    };

    private static string IconFor(string? name) => (name ?? string.Empty).ToUpperInvariant() switch
    {
        var x when x.Contains("APAC") => "bi-clipboard2-pulse",
        var x when x.Contains("LAUDO") => "bi-file-medical",
        var x when x.Contains("EXAME") => "bi-activity",
        var x when x.Contains("PRESCRI") => "bi-capsule",
        var x when x.Contains("CONTRATO") => "bi-file-earmark-ruled",
        var x when x.Contains("FINANCE") || x.Contains("FATURA") => "bi-cash-coin",
        _ => "bi-tag"
    };

    public sealed record UpdateClassificationRequest(Guid? ClassificationId, string? Reason);
    private sealed class QuickClassificationRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
