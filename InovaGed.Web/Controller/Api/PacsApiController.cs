using InovaGed.Application.Common.Context;
using InovaGed.Infrastructure.Pacs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers.Api;

[ApiController]
[Route("api/pacs")]
[Authorize]
public sealed class PacsApiController : ControllerBase
{
    private readonly PacsIntegrationService _svc;
    private readonly ICurrentContext _context;

    public PacsApiController(PacsIntegrationService svc, ICurrentContext context)
    {
        _svc = svc;
        _context = context;
    }

    [HttpPost("tickets")]
    [RequestSizeLimit(150_000_000)]
    public async Task<IActionResult> Create([FromForm] PacsTicketUploadRequest req, CancellationToken ct)
    {
        if (_context.TenantId == Guid.Empty)
            return Forbid();

        var ticketId = await _svc.CreateTicketAndUploadAsync(
            _context.TenantId,
            req.ProtocolCode,
            req.PatientName,
            req.PatientId,
            req.Modality,
            req.ExamType,
            req.StudyUid,
            req.Notes,
            req.Files,
            ct);

        return Ok(new { ticketId });
    }
}

public sealed class PacsTicketUploadRequest
{
    public string ProtocolCode { get; set; } = "";
    public string? PatientName { get; set; }
    public string? PatientId { get; set; }
    public string? Modality { get; set; }
    public string? ExamType { get; set; }
    public string? StudyUid { get; set; }
    public string? Notes { get; set; }

    public List<IFormFile> Files { get; set; } = new();
}
