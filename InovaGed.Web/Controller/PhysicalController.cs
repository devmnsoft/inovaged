using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Physical;
using InovaGed.Application.Identity;
using InovaGed.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class PhysicalController : Controller
{
    private readonly ILogger<PhysicalController> _logger;
    private readonly ICurrentUser _user;
    private readonly IPhysicalQueries _queries;
    private readonly IPhysicalCommands _commands;
    private readonly IDbConnectionFactory _dbFactory;
 
    public PhysicalController(
        ILogger<PhysicalController> logger,
        ICurrentUser user,
        IPhysicalQueries queries,
        IPhysicalCommands commands,
        IDbConnectionFactory dbFactory)
    {
        _logger = logger;
        _user = user;
        _queries = queries;
        _commands = commands;
        _dbFactory = dbFactory;
    }

    // =========================
    // ✅ Helper: abre conexão
    // =========================


    // ==========================================================
    // ---------- Locations (NÃO MEXI - volta a funcionar) -------
    // ==========================================================
    [HttpGet("Locations")]
    public async Task<IActionResult> Locations(string? q, CancellationToken ct)
    {
        try
        {
            var list = await _queries.ListLocationsAsync(_user.TenantId, q, ct);
            ViewBag.Q = q;
            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.Locations failed");
            return View(Array.Empty<PhysicalLocationRowDto>());
        }
    }

    [HttpGet("Locations/{id:guid}")]
    public async Task<IActionResult> EditLocation(Guid id, CancellationToken ct)
    {
        try
        {
            var vm = await _queries.GetLocationAsync(_user.TenantId, id, ct);
            if (vm is null) return NotFound();
            return View("LocationForm", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.EditLocation failed");
            return StatusCode(500);
        }
    }

    [HttpPost("Locations/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLocation(PhysicalLocationFormVM vm, CancellationToken ct)
    {
        try
        {
            var res = await _commands.UpsertLocationAsync(_user.TenantId, _user.UserId, vm, ct);
            if (!res.IsSuccess)
            {
                TempData["Err"] = res.ErrorMessage;
                return View("LocationForm", vm);
            }

            TempData["Ok"] = "Localização salva.";
            return RedirectToAction(nameof(Locations));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.SaveLocation failed");
            TempData["Err"] = "Erro ao salvar localização.";
            return View("LocationForm", vm);
        }
    }

    [HttpPost("Locations/{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLocation(Guid id, CancellationToken ct)
    {
        try
        {
            var res = await _commands.DeleteLocationAsync(_user.TenantId, id, _user.UserId, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Localização removida." : res.ErrorMessage;
            return RedirectToAction(nameof(Locations));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.DeleteLocation failed");
            TempData["Err"] = "Erro ao excluir localização.";
            return RedirectToAction(nameof(Locations));
        }
    }

    // ==========================================
    // ---------- Boxes (NÃO MEXI) --------------
    // ==========================================
    [HttpGet("Boxes")]
    public async Task<IActionResult> Boxes(string? q, CancellationToken ct)
    {
        try
        {
            var list = await _queries.ListBoxesAsync(_user.TenantId, q, ct);
            ViewBag.Q = q;
            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.Boxes failed");
            return View(Array.Empty<BoxRowDto>());
        }
    }

    [HttpGet("Boxes/{id:guid}")]
    public async Task<IActionResult> EditBox(Guid id, CancellationToken ct)
    {
        try
        {
            var vm = await _queries.GetBoxAsync(_user.TenantId, id, ct);
            if (vm is null) return NotFound();
            return View("BoxForm", vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.EditBox failed");
            return StatusCode(500);
        }
    }

    [HttpPost("Boxes/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBox(BoxFormVM vm, CancellationToken ct)
    {
        try
        {
            var res = await _commands.UpsertBoxAsync(_user.TenantId, _user.UserId, vm, ct);
            if (!res.IsSuccess)
            {
                TempData["Err"] = res.ErrorMessage;
                return View("BoxForm", vm);
            }

            TempData["Ok"] = "Caixa salva.";
            return RedirectToAction(nameof(Boxes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.SaveBox failed");
            TempData["Err"] = "Erro ao salvar caixa.";
            return View("BoxForm", vm);
        }
    }

    [HttpPost("Boxes/{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBox(Guid id, CancellationToken ct)
    {
        try
        {
            var res = await _commands.DeleteBoxAsync(_user.TenantId, id, _user.UserId, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Caixa removida." : res.ErrorMessage;
            return RedirectToAction(nameof(Boxes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.DeleteBox failed");
            TempData["Err"] = "Erro ao excluir caixa.";
            return RedirectToAction(nameof(Boxes));
        }
    }

    // ==========================================================
    // ✅ NOVO: Conteúdo da Caixa (PoC 17)
    // Usa: ged.box + ged.batch_item.box_id + ged.document
    // URL: GET /Physical/BoxContents?boxId=...
    // ==========================================================
    [HttpGet("BoxContents")]
    public async Task<IActionResult> BoxContents(Guid? boxId, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.OpenAsync(ct);

            ViewData["Title"] = "Conteúdo da Caixa";
            ViewData["Subtitle"] = "Visualize documentos vinculados (PoC 17)";

            // Tela de entrada (sem boxId)
            if (boxId is null || boxId == Guid.Empty)
            {
                return View(new BoxContentsVm
                {
                    BoxId = null,
                    Box = null,
                    Items = new List<BoxContentsVm.ItemRow>()
                });
            }

            // Carrega a caixa (não quebra se não existir reg_status)
            var box = await db.QueryFirstOrDefaultAsync<BoxContentsVm.BoxHeader>(
                """
            select
              id as "Id",
              label_code as "LabelCode",
              notes as "Notes",
              location_id as "LocationId"
            from ged.box
            where tenant_id=@tid
              and id=@boxId
              and (reg_status is null or reg_status='A')
            """,
                new { tid = _user.TenantId, boxId }
            );

            if (box is null)
                return NotFound("Caixa não encontrada.");

            // Itens: vínculo via batch_item.box_id
            var items = (await db.QueryAsync<BoxContentsVm.ItemRow>(
                """
            select
              bi.id         as "BatchItemId",
              bi.batch_id   as "BatchId",
              coalesce(b.batch_no,'') as "BatchNo",
              bi.document_id as "DocumentId",
              coalesce(d.code,'') as "Code",
              coalesce(d.title,'') as "Title",
              coalesce(d.status::text,'') as "Status",
              bi.created_at as "LinkedAt"
            from ged.batch_item bi
            join ged.batch b
              on b.tenant_id=bi.tenant_id
             and b.id=bi.batch_id
             and (b.reg_status is null or b.reg_status='A')
            join ged.document d
              on d.tenant_id=bi.tenant_id
             and d.id=bi.document_id
            where bi.tenant_id=@tid
              and (bi.reg_status is null or bi.reg_status='A')
              and bi.box_id=@boxId
            order by bi.created_at desc nulls last, b.batch_no, d.title
            """,
                new { tid = _user.TenantId, boxId }
            )).ToList();

            // Subtitle “bonito” com contagem
            ViewData["Subtitle"] = $"Caixa: {box.LabelCode} • Itens: {items.Count}";

            return View(new BoxContentsVm
            {
                BoxId = boxId,
                Box = box,
                Items = items
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.BoxContents failed");
            TempData["Err"] = "Erro ao carregar conteúdo da caixa.";
            return RedirectToAction(nameof(Boxes));
        }
    }

    // ==========================================================
    // ✅ NOVO: Adicionar documento à caixa
    // POST /Physical/BoxContents/Add
    // Estratégia PoC: vincula atualizando ged.batch_item.box_id
    // ==========================================================
    [HttpPost("BoxContents/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDocumentToBox(Guid boxId, Guid docId, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.OpenAsync(ct);

            // garante que caixa existe
            var okBox = await db.ExecuteScalarAsync<int>(@"
select count(1)
from ged.box
where tenant_id=@tid and id=@boxId and reg_status='A';",
                new { tid = _user.TenantId, boxId });

            if (okBox <= 0)
            {
                TempData["Err"] = "Caixa inválida.";
                return RedirectToAction(nameof(BoxContents), new { boxId });
            }

            // tenta vincular em algum batch_item do documento (mais recente)
            // (se não existir batch_item para o doc, não dá pra provar vínculo via esse modelo)
            var affected = await db.ExecuteAsync(@"
update ged.batch_item bi
set box_id=@boxId
where bi.tenant_id=@tid
  and bi.document_id=@docId
  and bi.reg_status='A'
  and bi.id = (
      select bi2.id
      from ged.batch_item bi2
      where bi2.tenant_id=@tid
        and bi2.document_id=@docId
        and bi2.reg_status='A'
      order by bi2.created_at desc
      limit 1
  );",
                new { tid = _user.TenantId, boxId, docId });

            if (affected <= 0)
                TempData["Err"] = "Não foi possível vincular: o documento não está em nenhum lote (batch_item) ativo.";

            else
                TempData["Ok"] = "Documento vinculado à caixa.";

            return RedirectToAction(nameof(BoxContents), new { boxId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.AddDocumentToBox failed");
            TempData["Err"] = "Erro ao vincular documento.";
            return RedirectToAction(nameof(BoxContents), new { boxId });
        }
    }

    // ==========================================================
    // ✅ NOVO: Remover documento da caixa
    // POST /Physical/BoxContents/Remove
    // ==========================================================
    [HttpPost("BoxContents/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDocumentFromBox(Guid boxId, Guid docId, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.OpenAsync(ct);

            var affected = await db.ExecuteAsync(@"
update ged.batch_item
set box_id = null
where tenant_id=@tid
  and reg_status='A'
  and box_id=@boxId
  and document_id=@docId;",
                new { tid = _user.TenantId, boxId, docId });

            TempData[affected > 0 ? "Ok" : "Err"] =
                affected > 0 ? "Documento removido da caixa." : "Nenhum vínculo ativo encontrado para remover.";

            return RedirectToAction(nameof(BoxContents), new { boxId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.RemoveDocumentFromBox failed");
            TempData["Err"] = "Erro ao remover documento.";
            return RedirectToAction(nameof(BoxContents), new { boxId });
        }
    }

    // ==========================================================
    // ✅ NOVO: Mover documento entre caixas
    // POST /Physical/BoxContents/Move
    // ==========================================================
    [HttpPost("BoxContents/Move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveDocumentBetweenBoxes(Guid fromBoxId, Guid toBoxId, Guid docId, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.OpenAsync(ct);

            // remove do vínculo atual (se existir)
            await db.ExecuteAsync(@"
update ged.batch_item
set box_id = null
where tenant_id=@tid
  and reg_status='A'
  and box_id=@fromBoxId
  and document_id=@docId;",
                new { tid = _user.TenantId, fromBoxId, docId });

            // adiciona no destino (usa o batch_item mais recente do doc)
            var affected = await db.ExecuteAsync(@"
update ged.batch_item bi
set box_id=@toBoxId
where bi.tenant_id=@tid
  and bi.document_id=@docId
  and bi.reg_status='A'
  and bi.id = (
      select bi2.id
      from ged.batch_item bi2
      where bi2.tenant_id=@tid
        and bi2.document_id=@docId
        and bi2.reg_status='A'
      order by bi2.created_at desc
      limit 1
  );",
                new { tid = _user.TenantId, toBoxId, docId });

            TempData[affected > 0 ? "Ok" : "Err"] =
                affected > 0 ? "Documento movido para a caixa destino." : "Não foi possível mover (documento sem batch_item ativo).";

            return RedirectToAction(nameof(BoxContents), new { boxId = toBoxId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical.MoveDocumentBetweenBoxes failed");
            TempData["Err"] = "Erro ao mover documento.";
            return RedirectToAction(nameof(BoxContents), new { boxId = toBoxId });
        }
    }

    // ==========================================================
    // ---------- BoxHistory (mantém como você já tinha) ---------
    // URL: GET /Physical/BoxHistory?boxId=...
    // ==========================================================
    [HttpGet("BoxHistory")]
    public async Task<IActionResult> BoxHistory(Guid boxId, CancellationToken ct)
    {
        if (boxId == Guid.Empty)
        {
            TempData["Err"] = "Selecione uma caixa.";
            return RedirectToAction(nameof(Boxes));
        }

        var rows = await _queries.GetBoxHistoryAsync(_user.TenantId, boxId, ct);

        ViewData["Title"] = "Histórico da Caixa";
        ViewData["Subtitle"] = $"Rastreio de documentos e fases (Caixa: {boxId})";

        return View(rows);
    }

    // Rotas auxiliares (como estava)
    [HttpGet("/Physical/Boxes/New")]
    public IActionResult NewBox() => View("BoxForm", new BoxFormVM());

    [HttpGet("/Physical/Locations/New")]
    public IActionResult NewLocation() => View("LocationForm", new PhysicalLocationFormVM());
}