using InovaGed.Infrastructure.Pacs;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers.Api;

[ApiController]
[Route("api/pacs")]
public sealed class PacsApiController : ControllerBase
{
    private readonly PacsIntegrationService _svc;

    // ajuste para seu current context de tenant, se já existir no projeto
    private Guid TenantId => Guid.Parse("00000000-0000-0000-0000-000000000001");

    public PacsApiController(PacsIntegrationService svc)
    {
        _svc = svc;
    }

    [HttpPost("tickets")]
    [RequestSizeLimit(1024L * 1024 * 1024)] // 1GB (ajuste)
    public async Task<IActionResult> Create([FromForm] PacsTicketUploadRequest req, CancellationToken ct)
    {
        var ticketId = await _svc.CreateTicketAndUploadAsync(
            TenantId,
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