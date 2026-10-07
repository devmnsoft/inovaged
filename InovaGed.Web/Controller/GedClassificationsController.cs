using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.GedAccess)]
public sealed class GedClassificationsController : Controller
{
    private static readonly string[] AllowedRoles = [AppRoles.Admin, AppRoles.AdministradorOphir, AppRoles.ArquivistaOphir, AppRoles.Arquivista];
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentClassificationCommands _commands;
    private readonly IDocumentBulkClassificationService _bulkClassification;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;
    private readonly ILogger<GedClassificationsController> _logger;

    public GedClassificationsController(
        IDbConnectionFactory db,
        IDocumentClassificationCommands commands,
        IDocumentBulkClassificationService bulkClassification,
        ICurrentUser currentUser,
        IAuditWriter audit,
        ILogger<GedClassificationsController> logger)
    {
        _db = db;
        _commands = commands;
        _bulkClassification = bulkClassification;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("/Ged/Classifications/QuickList")]
    public async Task<IActionResult> QuickList([FromQuery] string? q, [FromQuery] Guid? documentId, CancellationToken ct)
    {
        await using var con = await _db.OpenAsync(ct);
        var rows = (await con.QueryAsync<QuickClassificationRow>(new CommandDefinition("""
SELECT
  c.id AS "Id",
  c.code AS "Code",
  c.name AS "Name",
  c.description AS "Description",
  v.id AS "ClassificationVersionId",
  v.version_no AS "VersionNo"
FROM ged.classification_plan c
JOIN LATERAL (
    SELECT pv.id, pv.version_no
    FROM ged.classification_plan_version pv
    JOIN ged.classification_plan_version_item pvi
      ON pvi.tenant_id = pv.tenant_id
     AND pvi.version_id = pv.id
     AND pvi.classification_id = c.id
    WHERE pv.tenant_id = c.tenant_id
      AND COALESCE(pv.reg_status, 'A') = 'A'
      AND COALESCE(pvi.is_active, true)
      AND pv.version_no = (SELECT max(version_no) FROM ged.classification_plan_version WHERE tenant_id = c.tenant_id AND COALESCE(reg_status, 'A') = 'A')
    LIMIT 1
) v ON true
WHERE c.tenant_id = @TenantId
  AND COALESCE(c.reg_status, 'A') = 'A'
  AND COALESCE(c.is_active, true)
  AND (@Q IS NULL OR @Q = '' OR c.name ILIKE '%' || @Q || '%' OR c.code ILIKE '%' || @Q || '%' OR COALESCE(c.description, '') ILIKE '%' || @Q || '%')
ORDER BY c.code, lower(c.name)
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
                code = x.Code,
                name = string.IsNullOrWhiteSpace(x.Code) ? x.Name : $"{x.Code} — {x.Name}",
                description = x.Description,
                classificationVersionId = x.ClassificationVersionId,
                versionNo = x.VersionNo,
                color = ColorFor(x.Name),
                icon = IconFor(x.Name),
                suggestedByOcr = suggestedId == x.Id
            })
        });
    }

    [HttpPost("/Ged/Documents/{id:guid}/Classification")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDocumentClassification(Guid id, [FromBody] UpdateClassificationRequest? request, CancellationToken ct)
    {
        if (!AllowedRoles.Any(User.IsInRole)) return Forbid();
        if (id == Guid.Empty) return BadRequest(new { success = false, message = "Documento inválido." });
        request ??= new UpdateClassificationRequest(null, "Classificação rápida pela listagem");

        await using var con = await _db.OpenAsync(ct);
        var oldClassificationId = await con.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
SELECT COALESCE(dc.classification_id, d.classification_id)
FROM ged.document d
LEFT JOIN LATERAL (
    SELECT classification_id
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
SELECT c.name
FROM ged.classification_plan c
WHERE c.tenant_id = @TenantId
  AND c.id = @Id
  AND COALESCE(c.reg_status, 'A') = 'A'
  AND COALESCE(c.is_active, true)
  AND EXISTS (
      SELECT 1
      FROM ged.classification_plan_version v
      JOIN ged.classification_plan_version_item i
        ON i.tenant_id = v.tenant_id
       AND i.version_id = v.id
       AND i.classification_id = c.id
      WHERE v.tenant_id = c.tenant_id
        AND COALESCE(v.reg_status, 'A') = 'A'
        AND COALESCE(i.is_active, true)
        AND v.version_no = (SELECT max(version_no) FROM ged.classification_plan_version WHERE tenant_id = c.tenant_id AND COALESCE(reg_status, 'A') = 'A')
  )
LIMIT 1;
""", new { TenantId = _currentUser.TenantId, Id = newId.Value }, cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(label)) return BadRequest(new { success = false, message = "Classificação não encontrada." });

            var canonical = await _bulkClassification.ApplyAsync(_currentUser.TenantId, _currentUser.UserId, new[] { id }, newId.Value, ct);
            var item = canonical.Items.FirstOrDefault();
            if (item is null || !item.Success) return BadRequest(new { success = false, message = item?.Message ?? "Documento ou classificação indisponível para este tenant.", code = item?.Code });
        }
        else
        {
            await using var tx = con.BeginTransaction();
            var applied = await con.ExecuteScalarAsync<int>(new CommandDefinition("""
WITH selected_version AS (
    SELECT v.id
    FROM ged.classification_plan_version v
    JOIN ged.classification_plan_version_item i
      ON i.tenant_id = v.tenant_id
     AND i.version_id = v.id
     AND i.classification_id = @ClassificationId
    WHERE v.tenant_id = @TenantId
      AND @ClassificationId IS NOT NULL
      AND COALESCE(v.reg_status, 'A') = 'A'
      AND COALESCE(i.is_active, true)
    ORDER BY v.version_no DESC, v.published_at DESC NULLS LAST, v.created_at DESC
    LIMIT 1
),
updated_document AS (
    UPDATE ged.document d
    SET classification_id = @ClassificationId,
        classification_version_id = (SELECT id FROM selected_version),
        updated_at = now(),
        updated_by = @UserId
    WHERE d.tenant_id = @TenantId
      AND d.id = @DocumentId
      AND COALESCE(d.reg_status, 'A') = 'A'
      AND (@ClassificationId IS NULL OR EXISTS (SELECT 1 FROM selected_version))
    RETURNING d.id, d.current_version_id
),
upsert_classification AS (
    INSERT INTO ged.document_classification
      (document_id, tenant_id, document_version_id, classification_id, classification_version_id, confidence, method, summary, classified_at, classified_by, source, updated_at, reg_status)
    SELECT id, @TenantId, current_version_id, @ClassificationId, (SELECT id FROM selected_version), NULL, 'MANUAL', @Reason, now(), @UserId, 'WEB_QUICK_CLASSIFICATION', now(), 'A'
    FROM updated_document
    WHERE @ClassificationId IS NOT NULL
    ON CONFLICT (document_id)
    DO UPDATE SET
      tenant_id = EXCLUDED.tenant_id,
      document_version_id = EXCLUDED.document_version_id,
      classification_id = EXCLUDED.classification_id,
      classification_version_id = EXCLUDED.classification_version_id,
      confidence = NULL,
      method = 'MANUAL',
      summary = EXCLUDED.summary,
      classified_at = now(),
      classified_by = @UserId,
      source = 'WEB_QUICK_CLASSIFICATION',
      updated_at = now(),
      reg_status = 'A'
    WHERE ged.document_classification.tenant_id = EXCLUDED.tenant_id
    RETURNING document_id
),
removed_classification AS (
    UPDATE ged.document_classification dc
    SET classification_id = NULL,
        classification_version_id = NULL,
        confidence = NULL,
        method = 'MANUAL',
        summary = @Reason,
        classified_at = now(),
        classified_by = @UserId,
        source = 'WEB_QUICK_CLASSIFICATION',
        updated_at = now(),
        reg_status = 'A'
    WHERE dc.tenant_id = @TenantId
      AND dc.document_id = @DocumentId
      AND @ClassificationId IS NULL
      AND EXISTS (SELECT 1 FROM updated_document)
    RETURNING document_id
),
pending_recalc AS (
    INSERT INTO ged.ai_retention_recalc_pending (id, tenant_id, document_id, application_id, reason, attempts, next_attempt_at)
    SELECT gen_random_uuid(), @TenantId, @DocumentId, NULL, 'QUICK_CLASSIFICATION_RECALC', 0, now()
    WHERE EXISTS (SELECT 1 FROM updated_document)
      AND NOT EXISTS (
          SELECT 1 FROM ged.ai_retention_recalc_pending
          WHERE tenant_id = @TenantId
            AND document_id = @DocumentId
            AND application_id IS NULL
            AND resolved_at IS NULL
      )
    RETURNING id
)
SELECT count(*) FROM updated_document;
""", new
            {
                TenantId = _currentUser.TenantId,
                DocumentId = id,
                ClassificationId = newId,
                UserId = _currentUser.UserId,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Classificação rápida pela listagem" : request.Reason
            }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            if (applied == 0) return BadRequest(new { success = false, message = "Documento ou classificação indisponível para este tenant." });
        }

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
        public string? Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? ClassificationVersionId { get; set; }
        public int? VersionNo { get; set; }
    }
}
