using InovaGed.Application.Signatures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[ApiController]
[Authorize(Policy = "SignatureCmsCreate")]
public sealed class SigningApiController(ISigningOrchestrator orchestrator) : ControllerBase
{
    [HttpPost("/api/signing/sessions")]
    public async Task<IActionResult> Create([FromBody] PrepareSignatureCommand command, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await orchestrator.PrepareAsync(command, ct));
    }

    [HttpGet("/api/signing/sessions/{id:guid}")]
    public IActionResult Get(Guid id) { Response.Headers.CacheControl = "no-store"; return Ok(new { id, status = "REQUESTED" }); }

    [HttpGet("/api/signing/sessions/{id:guid}/content")]
    public IActionResult Content(Guid id, [FromHeader(Name = "X-InovaGed-Content-Token")] string token)
    { Response.Headers.CacheControl = "no-store"; return Unauthorized(new { error = "Content repository must validate one-time token before streaming the exact version." }); }

    [HttpPost("/api/signing/sessions/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteSignatureCommand command, CancellationToken ct)
    { Response.Headers.CacheControl = "no-store"; return Ok(await orchestrator.CompleteAsync(command with { SessionId = id }, ct)); }

    [HttpPost("/api/signing/sessions/{id:guid}/cancel")]
    public IActionResult Cancel(Guid id) { Response.Headers.CacheControl = "no-store"; return NoContent(); }

    [HttpGet("/api/signatures/{id:guid}")]
    [Authorize(Policy = "SignatureView")]
    public IActionResult Signature(Guid id) { Response.Headers.CacheControl = "no-store"; return Ok(new { id, type = "CMS_DETACHED", conformityStatus = "NOT_EVALUATED" }); }

    [HttpGet("/api/signatures/{id:guid}/validation")]
    [Authorize(Policy = "SignatureValidate")]
    public IActionResult Validation(Guid id) { Response.Headers.CacheControl = "no-store"; return Ok(new { id, conformityStatus = "NOT_EVALUATED" }); }

    [HttpGet("/api/signatures/{id:guid}/download")]
    [Authorize(Policy = "SignatureDownload")]
    public IActionResult Download(Guid id) { Response.Headers.CacheControl = "no-store"; return NotFound(); }

    [HttpGet("/api/signatures/{id:guid}/package")]
    [Authorize(Policy = "SignatureDownload")]
    public IActionResult Package(Guid id) { Response.Headers.CacheControl = "no-store"; return NotFound(); }
}
