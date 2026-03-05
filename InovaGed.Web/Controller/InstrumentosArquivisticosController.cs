using Dapper;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

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

    // =========================================================
    // MOVIMENTAR CÓDIGO (PCD/TTD)
    // GET /InstrumentosArquivisticos/MoveCode
    // =========================================================
    [HttpGet("MoveCode")]
    public async Task<IActionResult> MoveCode(CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        await using var conn = await _db.OpenAsync(ct);

        var classes = (await conn.QueryAsync<ClassificationPickRow>(@"
            SELECT id   AS Id,
                   code AS Code,
                   name AS Name
              FROM ged.classification_plan
             WHERE tenant_id = @tenantId
               AND is_active = true
             ORDER BY code;",
            new { tenantId }))
            .ToList();

        var vm = new MoveCodeVM { Classes = classes };

        ViewData["Title"] = "Movimentar Código (PCD/TTD)";
        ViewData["Subtitle"] = "Reclassificar/mover classe mantendo todo o conteúdo (inclui filhos).";
        return View(vm);
    }

    // =========================================================
    // MOVIMENTAR CÓDIGO (PCD/TTD)
    // POST /InstrumentosArquivisticos/MoveCode
    // =========================================================
    [HttpPost("MoveCode")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveCode(MoveCodeVM vm, CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        var actorId = ActorIdOrEmpty();

        // validação mínima
        if (vm.ClassificationId == Guid.Empty)
        {
            TempData["Err"] = "Selecione a classe a movimentar.";
            return await ReloadWithClasses(vm, ct);
        }

        // evita "pai = ele mesmo" (ciclo imediato)
        if (vm.NewParentId.HasValue && vm.NewParentId.Value == vm.ClassificationId)
        {
            TempData["Err"] = "O novo pai não pode ser o próprio item selecionado.";
            return await ReloadWithClasses(vm, ct);
        }

        var newCode = string.IsNullOrWhiteSpace(vm.NewCode) ? null : vm.NewCode.Trim();
        var reason = string.IsNullOrWhiteSpace(vm.Reason) ? null : vm.Reason.Trim();

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            // ✅ A função deve retornar 1 linha com o resultado (antes/depois)
            var result = await conn.QuerySingleAsync<MoveCodeResultRow>(@"
                SELECT *
                  FROM ged.move_classification_code(
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
                    newParentId = vm.NewParentId, // null = raiz
                    newCode,                      // null = mantém o antigo
                    actor = actorId == Guid.Empty ? (Guid?)null : actorId,
                    reason
                });

            // ✅ Mensagem resumida
            TempData["Ok"] = "Movimentação executada com sucesso.";

            TempData["MoveSummary"] =
                $"Código: {result.OldCode} → {result.NewCode} | Afetados: {result.AffectedCount}";

            // ✅ Detalhe completo (mostra exatamente o que foi aplicado)
            TempData["MoveDetails"] =
                $"Classe: {result.ClassificationId}\n" +
                $"Código: {result.OldCode} → {result.NewCode}\n" +
                $"Pai: {(result.OldParentId?.ToString() ?? "RAIZ")} → {(result.NewParentId?.ToString() ?? "RAIZ")}\n" +
                $"Afetados (classe + descendentes): {result.AffectedCount}\n" +
                $"Data/Hora: {result.MovedAt:dd/MM/yyyy HH:mm:ss}";

            return RedirectToAction(nameof(MoveCode));
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            // validações do RAISE EXCEPTION na função
            _logger.LogWarning(ex, "MoveCode validation failed Tenant={Tenant}", tenantId);
            TempData["Err"] = ex.MessageText;
            return await ReloadWithClasses(vm, ct);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "MoveCode postgres failed Tenant={Tenant}", tenantId);
            TempData["Err"] = $"Erro no banco: {ex.MessageText}";
            return await ReloadWithClasses(vm, ct);
        }
        catch (InvalidOperationException ex)
        {
            // QuerySingleAsync pode estourar se vier 0 linhas
            _logger.LogError(ex, "MoveCode returned no row Tenant={Tenant}", tenantId);
            TempData["Err"] = "A movimentação foi executada, mas a função não retornou o resultado esperado (0 linhas). Ajuste a função para retornar o 'antes/depois'.";
            return await ReloadWithClasses(vm, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveCode failed Tenant={Tenant}", tenantId);
            TempData["Err"] = "Falha ao movimentar o código. Verifique os dados e tente novamente.";
            return await ReloadWithClasses(vm, ct);
        }
    }

    private async Task<IActionResult> ReloadWithClasses(MoveCodeVM vm, CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        await using var conn = await _db.OpenAsync(ct);

        vm.Classes = (await conn.QueryAsync<ClassificationPickRow>(@"
            SELECT id AS Id, code AS Code, name AS Name
              FROM ged.classification_plan
             WHERE tenant_id = @tenantId
               AND is_active = true
             ORDER BY code;",
            new { tenantId }))
            .ToList();

        ViewData["Title"] = "Movimentar Código (PCD/TTD)";
        ViewData["Subtitle"] = "Reclassificar/mover classe mantendo todo o conteúdo (inclui filhos).";
        return View(vm);
    }

    // =========================================================
    // VERSÕES DO PCD/TTD (snapshot)
    // GET /InstrumentosArquivisticos/Versions
    // =========================================================
    [HttpGet("Versions")]
    public async Task<IActionResult> Versions(CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        await using var conn = await _db.OpenAsync(ct);

        var list = (await conn.QueryAsync<PlanVersionRow>(@"
            SELECT id AS Id, version_no AS VersionNo, title AS Title, notes AS Notes,
                   published_at AS PublishedAt, published_by AS PublishedBy
              FROM ged.classification_plan_version
             WHERE tenant_id = @tenantId
             ORDER BY version_no DESC;",
            new { tenantId }))
            .ToList();

        ViewData["Title"] = "Versões do PCD/TTD";
        ViewData["Subtitle"] = "Snapshots publicados para visualizar, imprimir e comparar.";
        return View(list);
    }

    // GET /InstrumentosArquivisticos/Versions/New
    [HttpGet("Versions/New")]
    public IActionResult NewVersion()
    {
        ViewData["Title"] = "Publicar nova versão do PCD/TTD";
        return View(new PublishVersionVM());
    }

    // POST /InstrumentosArquivisticos/Versions/New
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
            await using var conn = await _db.OpenAsync(ct);

            var nextNo = await conn.ExecuteScalarAsync<int>(@"
                SELECT COALESCE(MAX(version_no),0) + 1
                  FROM ged.classification_plan_version
                 WHERE tenant_id = @tenantId;",
                new { tenantId });

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
                WHERE cp.tenant_id = @tenantId
                ORDER BY cp.code;",
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

    // GET /InstrumentosArquivisticos/Versions/{id}
    [HttpGet("Versions/{id:guid}")]
    public async Task<IActionResult> VersionDetails(Guid id, CancellationToken ct)
    {
        var tenantId = TenantIdOrThrow();
        await using var conn = await _db.OpenAsync(ct);

        var header = await conn.QuerySingleOrDefaultAsync<PlanVersionRow>(@"
            SELECT id AS Id, version_no AS VersionNo, title AS Title, notes AS Notes,
                   published_at AS PublishedAt, published_by AS PublishedBy
              FROM ged.classification_plan_version
             WHERE tenant_id=@tenantId AND id=@id;",
            new { tenantId, id });

        if (header is null) return NotFound();

        var items = (await conn.QueryAsync<PlanVersionItemRow>(@"
            SELECT code AS Code, name AS Name, parent_code AS ParentCode,
                   retention_start_event::text AS RetentionStartEvent,
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
             ORDER BY code;",
            new { tenantId, id }))
            .ToList();

        var vm = new VersionDetailsVM { Header = header, Items = items };

        ViewData["Title"] = $"Versão #{header.VersionNo}";
        ViewData["Subtitle"] = header.Title;
        return View(vm);
    }

    // =========================================================
    // Rows / VMs
    // =========================================================
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
        public string? NewCode { get; set; }     // opcional
        public string? Reason { get; set; }
        public List<ClassificationPickRow> Classes { get; set; } = new();
    }

    // ✅ retorno esperado da função ged.move_classification_code(...)
    public sealed class MoveCodeResultRow
    {
        public Guid ClassificationId { get; set; }
        public string OldCode { get; set; } = "";
        public string NewCode { get; set; } = "";
        public Guid? OldParentId { get; set; }
        public Guid? NewParentId { get; set; }
        public int AffectedCount { get; set; }
        public DateTimeOffset MovedAt { get; set; }
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