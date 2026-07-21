using System.Security.Cryptography;
using InovaGed.Application.Signatures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[ApiController]
[Authorize(Policy = "SignatureCmsCreate")]
public sealed class SigningApiController(ISigningOrchestrator orchestrator) : ControllerBase
{
    [HttpPost("/api/signing/sessions")]
    public async Task<IActionResult> Create([FromBody] CreateSigningSessionRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var tenantId = ReadGuidClaim("tenant_id");
        var userId = ReadGuidClaim("sub") ?? ReadGuidClaim(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (tenantId is null || userId is null) return Forbid();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var command = new PrepareSignatureCommand(tenantId.Value, userId.Value, request.DocumentId, request.DocumentVersionId, SignatureType.CMS_DETACHED, "CMS_PKCS7_DETACHED", null, string.Empty, "SHA-256", nonce, DateTimeOffset.UtcNow.AddMinutes(10), HttpContext.TraceIdentifier);
        return Ok(await orchestrator.PrepareAsync(command, ct));
    }

    [HttpGet("/api/signing/sessions/{id:guid}")]
    public IActionResult Get(Guid id) { Response.Headers.CacheControl = "no-store"; return Ok(new { id, status = "REQUESTED" }); }

    [HttpGet("/api/signing/sessions/{id:guid}/content")]
    public IActionResult Content(Guid id, [FromHeader(Name = "X-InovaGed-Content-Token")] string token)
    { Response.Headers.CacheControl = "no-store"; return Unauthorized(new { error = "Content repository must validate one-time token before streaming the exact version." }); }

    [HttpPost("/api/signing/sessions/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteSigningSessionRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var command = new CompleteSignatureCommand(id, Convert.FromBase64String(request.SignatureCmsBase64), Convert.FromBase64String(request.CertificateDerBase64), request.CertificateChainDerBase64.Select(Convert.FromBase64String).ToArray(), null, new Dictionary<string, string> { ["idempotency_key"] = request.IdempotencyKey, ["agent_operation_id"] = request.AgentOperationId, ["agent_version"] = request.AgentVersion });
        return Ok(await orchestrator.CompleteAsync(command, ct));
    }

    private Guid? ReadGuidClaim(string type) => Guid.TryParse(User.FindFirst(type)?.Value, out var value) ? value : null;

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
