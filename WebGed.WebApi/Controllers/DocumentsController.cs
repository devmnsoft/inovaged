using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebGed.Application.Documents;

namespace WebGed.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DocumentsController : ControllerBase
{
    private readonly DocumentAppService _svc;

    public DocumentsController(DocumentAppService svc) => _svc = svc;

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromBody] UploadDocumentCommand cmd, CancellationToken ct)
    {
        var result = await _svc.UploadAsync(cmd, ct);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(new { documentId = result.Value });
    }
}
