using InovaGed.Application.Pacs;
using InovaGed.Infrastructure.Pacs;
using InovaGed.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InovaGed.Web.Controllers;

[Route("[controller]")]
public sealed class PacsController : Controller
{
    private readonly ITicketRepository _repo;
    private readonly PacsIntegrationService _svc;
    private readonly StorageLocalOptions _storage;

    private Guid TenantId => Guid.Parse("00000000-0000-0000-0000-000000000001");

    public PacsController(ITicketRepository repo, PacsIntegrationService svc, IOptions<StorageLocalOptions> storage)
    {
        _repo = repo;
        _svc = svc;
        _storage = storage.Value;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var list = await _repo.ListTicketsAsync(TenantId, q, ct);
        ViewBag.Q = q;
        return View(list);
    }

    [HttpGet("Novo")]
    public IActionResult Novo() => View();

    [HttpPost("Novo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NovoPost(PacsMvcCreateVM vm, CancellationToken ct)
    {
        var ticketId = await _svc.CreateTicketAndUploadAsync(
            TenantId,
            vm.ProtocolCode,
            vm.PatientName,
            vm.PatientId,
            vm.Modality,
            vm.ExamType,
            vm.StudyUid,
            vm.Notes,
            vm.Files ?? new List<IFormFile>(),
            ct);

        return RedirectToAction(nameof(Details), new { id = ticketId });
    }

    [HttpGet("Detalhes/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var ticket = await _repo.GetTicketAsync(TenantId, id, ct);
        if (ticket is null) return NotFound();

        var files = await _repo.ListFilesAsync(TenantId, id, ct);
        ViewBag.Ticket = ticket.Value;
        return View(files);
    }

    // Serve o arquivo do disco (para <img>, download, etc)
    [HttpGet("Arquivo/{fileId:guid}")]
    public async Task<IActionResult> File(Guid fileId, CancellationToken ct)
    {
        var file = await _repo.GetFileAsync(TenantId, fileId, ct);
        if (file is null) return NotFound();

        var abs = Path.Combine(_storage.RootPath, file.StorageRelPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (!System.IO.File.Exists(abs)) return NotFound();

        return PhysicalFile(abs, file.ContentType, file.OriginalFileName);
    }
}

public sealed class PacsMvcCreateVM
{
    public string ProtocolCode { get; set; } = "";
    public string? PatientName { get; set; }
    public string? PatientId { get; set; }
    public string? Modality { get; set; }
    public string? ExamType { get; set; }
    public string? StudyUid { get; set; }
    public string? Notes { get; set; }
    public List<IFormFile>? Files { get; set; }
}