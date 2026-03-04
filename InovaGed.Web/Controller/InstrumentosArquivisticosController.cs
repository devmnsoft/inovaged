using Dapper;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class InstrumentosArquivisticosController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentContext _ctx;
    private readonly ILogger<InstrumentosArquivisticosController> _logger;

    public InstrumentosArquivisticosController(
        IDbConnectionFactory db,
        ICurrentContext ctx,
        ILogger<InstrumentosArquivisticosController> logger)
    {
        _db = db;
        _ctx = ctx;
        _logger = logger;
    }

    private Guid TenantIdOrThrow()
    {
        if (_ctx.TenantId == Guid.Empty)
            throw new InvalidOperationException("TenantId não encontrado no contexto.");
        return _ctx.TenantId;
    }

    private Guid ActorIdOrEmpty() => _ctx.UserId;

    // ---------------------------
    // MOVIMENTAR CÓDIGO (PCD/TTD)
    // ---------------------------
    [HttpGet("MoveCode")]
    public async Task<IActionResult> MoveCode(CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();

        using var conn = await _db.OpenAsync(ct);

        // Lista simples para dropdown (PoC). Pode virar tree depois.
        var classes = (await conn.QueryAsync<ClassificationPickRow>(@"
            SELECT id AS Id, code AS Code, name AS Name
              FROM ged.classification_plan
             WHERE tenant_id = @tenantId
             ORDER BY code;", new { tenantId }))
            .ToList();

        var vm = new MoveCodeVM { Classes = classes };
        ViewData["Title"] = "Movimentar Código (PCD/TTD)";
        ViewData["Subtitle"] = "Reclassificar/mover classe mantendo todo o conteúdo (inclui filhos).";

        return View(vm);
    }

    [HttpPost("MoveCode")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveCode(MoveCodeVM vm, CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        var actor = ActorIdOrEmpty();

        if (vm.ClassificationId == Guid.Empty || string.IsNullOrWhiteSpace(vm.NewCode))
        {
            TempData["Err"] = "Selecione a classe e informe o novo código.";
            return RedirectToAction(nameof(MoveCode));
        }

        try
        {
            using var conn = await _db.OpenAsync(ct);

            // chama função do banco (transacional e segura)
            await conn.ExecuteAsync(@"
                SELECT ged.move_classification_code(
                    @tenantId,
                    @classificationId,
                    @newParentId,
                    @newCode,
                    @actor,
                    @reason
                );",
                new
                {
                    tenantId,
                    classificationId = vm.ClassificationId,
                    newParentId = vm.NewParentId, // pode ser null
                    newCode = vm.NewCode.Trim(),
                    actor = actor == Guid.Empty ? (Guid?)null : actor,
                    reason = string.IsNullOrWhiteSpace(vm.Reason) ? null : vm.Reason.Trim()
                });

            TempData["Ok"] = "Código movimentado com sucesso (classe e descendentes atualizados).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveCode failed Tenant={Tenant}", tenantId);
            TempData["Err"] = ex.Message;
        }

        return RedirectToAction(nameof(MoveCode));
    }

    // ---------------------------
    // VERSÕES DO PCD/TTD (snapshot)
    // ---------------------------
    [HttpGet("Versions")]
    public async Task<IActionResult> Versions(CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        using var conn = await _db.OpenAsync(ct);

        var list = (await conn.QueryAsync<PlanVersionRow>(@"
            SELECT id AS Id, version_no AS VersionNo, title AS Title, notes AS Notes,
                   published_at AS PublishedAt, published_by AS PublishedBy
              FROM ged.classification_plan_version
             WHERE tenant_id = @tenantId
             ORDER BY version_no DESC;", new { tenantId }))
            .ToList();

        ViewData["Title"] = "Versões do PCD/TTD";
        ViewData["Subtitle"] = "Snapshots publicados para visualizar, imprimir e comparar.";
        return View(list);
    }

    [HttpGet("Versions/New")]
    public IActionResult NewVersion()
    {
        ViewData["Title"] = "Publicar nova versão do PCD/TTD";
        return View(new PublishVersionVM());
    }

    [HttpPost("Versions/New")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewVersion(PublishVersionVM vm, CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        var actor = ActorIdOrEmpty();

        if (string.IsNullOrWhiteSpace(vm.Title))
        {
            TempData["Err"] = "Informe o título da versão.";
            return View(vm);
        }

        try
        {
            using var conn = await _db.OpenAsync(ct);

            // pega próximo version_no
            var nextNo = await conn.ExecuteScalarAsync<int>(@"
                SELECT COALESCE(MAX(version_no),0) + 1
                  FROM ged.classification_plan_version
                 WHERE tenant_id = @tenantId;", new { tenantId });

            var versionId = Guid.NewGuid();

            await conn.ExecuteAsync(@"
                INSERT INTO ged.classification_plan_version(
                    id, tenant_id, version_no, title, notes, published_at, published_by
                )
                VALUES (
                    @id, @tenantId, @no, @title, @notes, now(), @by
                );",
                new
                {
                    id = versionId,
                    tenantId,
                    no = nextNo,
                    title = vm.Title.Trim(),
                    notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
                    by = actor == Guid.Empty ? (Guid?)null : actor
                });

            // snapshot das classes (inclui parent_code)
            await conn.ExecuteAsync(@"
                INSERT INTO ged.classification_plan_version_item(
                    tenant_id, version_id, classification_id,
                    code, name, description, parent_code,
                    retention_start_event,
                    retention_active_days, retention_active_months, retention_active_years,
                    retention_archive_days, retention_archive_months, retention_archive_years,
                    final_destination, requires_digital_signature,
                    is_confidential, is_active, retention_notes
                )
                SELECT
                    cp.tenant_id, @versionId, cp.id,
                    cp.code, cp.name, cp.description,
                    parent.code AS parent_code,
                    cp.retention_start_event,
                    cp.retention_active_days, cp.retention_active_months, cp.retention_active_years,
                    cp.retention_archive_days, cp.retention_archive_months, cp.retention_archive_years,
                    cp.final_destination::ged.final_destination,
                    cp.requires_digital_signature,
                    cp.is_confidential, cp.is_active, cp.retention_notes
                FROM ged.classification_plan cp
                LEFT JOIN ged.classification_plan parent
                  ON parent.tenant_id = cp.tenant_id
                 AND parent.id = cp.parent_id
                WHERE cp.tenant_id = @tenantId;",
                new { tenantId, versionId });

            TempData["Ok"] = $"Versão publicada: #{nextNo}.";
            return RedirectToAction(nameof(VersionDetails), new { id = versionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publish version failed Tenant={Tenant}", tenantId);
            TempData["Err"] = ex.Message;
            return View(vm);
        }
    }

    [HttpGet("Versions/{id:guid}")]
    public async Task<IActionResult> VersionDetails(Guid id, CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        using var conn = await _db.OpenAsync(ct);

        var header = await conn.QuerySingleOrDefaultAsync<PlanVersionRow>(@"
            SELECT id AS Id, version_no AS VersionNo, title AS Title, notes AS Notes,
                   published_at AS PublishedAt, published_by AS PublishedBy
              FROM ged.classification_plan_version
             WHERE tenant_id=@tenantId AND id=@id;", new { tenantId, id });

        if (header is null) return NotFound();

        var items = (await conn.QueryAsync<PlanVersionItemRow>(@"
            SELECT code AS Code, name AS Name, parent_code AS ParentCode,
                   retention_start_event AS RetentionStartEvent,
                   retention_active_days AS ActiveDays,
                   retention_active_months AS ActiveMonths,
                   retention_active_years AS ActiveYears,
                   retention_archive_days AS ArchiveDays,
                   retention_archive_months AS ArchiveMonths,
                   retention_archive_years AS ArchiveYears,
                   final_destination::text AS FinalDestination,
                   requires_digital_signature AS RequiresDigitalSignature,
                   is_confidential AS IsConfidential,
                   is_active AS IsActive
              FROM ged.classification_plan_version_item
             WHERE tenant_id=@tenantId AND version_id=@id
             ORDER BY code;", new { tenantId, id }))
            .ToList();

        var vm = new VersionDetailsVM { Header = header, Items = items };

        ViewData["Title"] = $"Versão #{header.VersionNo}";
        ViewData["Subtitle"] = header.Title;
        return View(vm);
    }

    // -------------- VMs/Rows --------------
    public sealed class ClassificationPickRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => $"{Code} - {Name}";
    }

    public sealed class MoveCodeVM
    {
        public Guid ClassificationId { get; set; }
        public Guid? NewParentId { get; set; }
        public string NewCode { get; set; } = "";
        public string? Reason { get; set; }
        public List<ClassificationPickRow> Classes { get; set; } = new();
    }

    public sealed class PlanVersionRow
    {
        public Guid Id { get; set; }
        public int VersionNo { get; set; }
        public string Title { get; set; } = "";
        public string? Notes { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
        public Guid? PublishedBy { get; set; }
    }

    public sealed class PublishVersionVM
    {
        public string Title { get; set; } = "";
        public string? Notes { get; set; }
    }

    public sealed class PlanVersionItemRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? ParentCode { get; set; }

        public string RetentionStartEvent { get; set; } = "";
        public int ActiveDays { get; set; }
        public int ActiveMonths { get; set; }
        public int ActiveYears { get; set; }
        public int ArchiveDays { get; set; }
        public int ArchiveMonths { get; set; }
        public int ArchiveYears { get; set; }

        public string FinalDestination { get; set; } = "";
        public bool RequiresDigitalSignature { get; set; }
        public bool IsConfidential { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class VersionDetailsVM
    {
        public PlanVersionRow Header { get; set; } = new();
        public List<PlanVersionItemRow> Items { get; set; } = new();
    }
}