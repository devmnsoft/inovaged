using System.Security.Cryptography;
using InovaGed.Application.Signatures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InovaGed.Web.Controllers;

[ApiController]
public sealed class SigningApiController(
    ISigningOrchestrator orchestrator,
    ISigningSessionRepository sessions,
    IDocumentVersionSigningContentService content,
    ISignatureRepository signatures,
    ISignatureValidationRepository validations,
    ISignaturePackageService packages,
    IOptions<DigitalSignatureOptions> options) : ControllerBase
{
    [HttpPost("/api/signing/sessions")]
    [Authorize(Policy = "SignatureCmsCreate")]
    public async Task<IActionResult> Create([FromBody] CreateSigningSessionRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var tenantId = ReadGuidClaim("tenant_id");
        var userId = ReadGuidClaim("sub") ?? ReadGuidClaim(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (tenantId is null || userId is null) return Forbid();
        var command = new PrepareSigningSessionCommand(tenantId.Value, userId.Value, request.DocumentId, request.DocumentVersionId, request.Purpose, HttpContext.TraceIdentifier, Hash(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"), Hash(Request.Headers.UserAgent.ToString()));
        try
        {
            return Ok(await orchestrator.PrepareAsync(command, ct));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }

    [HttpGet("/api/signing/sessions/{id:guid}")]
    [Authorize(Policy = "SignatureView")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) { Response.Headers.CacheControl = "no-store"; var tenantId=ReadGuidClaim("tenant_id"); if(tenantId is null) return Forbid(); var s=await sessions.GetAsync(tenantId.Value,id,ct); return s is null ? NotFound() : Ok(s); }

    [HttpGet("/api/signing/sessions/{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, [FromHeader(Name = "X-InovaGed-Content-Token")] string token, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(token)) return NotFound();
        var tokenHash = Hash(token);
        var s = await sessions.ConsumeContentCapabilityAsync(id, tokenHash, ct);
        if (s is null) return NotFound();
        var meta = await content.GetMetadataAsync(s.TenantId, s.DocumentId, s.DocumentVersionId, ct);
        var stream = await content.OpenReadAsync(s.TenantId, s.DocumentId, s.DocumentVersionId, ct);
        Response.Headers.ContentDisposition = $"attachment; filename=\"{meta.FileName.Replace("\"", "")}\"";
        return File(stream, meta.ContentType, enableRangeProcessing: false);
    }

    [HttpPost("/api/signing/sessions/{id:guid}/complete")]
    [Authorize(Policy = "SignatureCmsCreate")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteSigningSessionRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryDecode(request.SignatureCmsBase64, options.Value.MaxDocumentSizeMb * 1024 * 1024, out var cms, out var error) || !TryDecode(request.CertificateDerBase64, 1024 * 1024, out var cert, out error)) return Problem(error, statusCode: 400);
        var chain = new List<byte[]>(); foreach (var item in request.CertificateChainDerBase64.Take(10)) { if(!TryDecode(item,1024*1024,out var der,out error)) return Problem(error,statusCode:400); chain.Add(der); }
        var tenantId = ReadGuidClaim("tenant_id"); var userId = ReadGuidClaim("sub") ?? ReadGuidClaim(System.Security.Claims.ClaimTypes.NameIdentifier); if (tenantId is null || userId is null) return Forbid();
        var command = new CompleteSigningSessionCommand(tenantId.Value, userId.Value, id, request.CompletionToken, request.IdempotencyKey, cms, cert, chain, request.AgentOperationId, request.AgentVersion, HttpContext.TraceIdentifier);
        return Ok(await orchestrator.CompleteAsync(command, ct));
    }

    [HttpPost("/api/signing/sessions/{id:guid}/cancel")]
    [Authorize(Policy = "SignatureCmsCreate")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { Response.Headers.CacheControl = "no-store"; var tenantId=ReadGuidClaim("tenant_id"); var userId=ReadGuidClaim("sub") ?? ReadGuidClaim(System.Security.Claims.ClaimTypes.NameIdentifier); if(tenantId is null||userId is null) return Forbid(); return await sessions.CancelAsync(tenantId.Value,id,userId.Value,ct) ? NoContent() : NotFound(); }
    [HttpGet("/api/signatures/{id:guid}")][Authorize(Policy = "SignatureView")] public async Task<IActionResult> Signature(Guid id,CancellationToken ct){ var tenantId=ReadGuidClaim("tenant_id"); if(tenantId is null) return Forbid(); var s=await signatures.GetAsync(tenantId.Value,id,ct); return s is null?NotFound():Ok(s); }
    [HttpGet("/api/signatures/{id:guid}/validation")][Authorize(Policy = "SignatureValidate")] public async Task<IActionResult> Validation(Guid id,CancellationToken ct){ var tenantId=ReadGuidClaim("tenant_id"); if(tenantId is null) return Forbid(); var r=await validations.GetLatestAsync(tenantId.Value,id,ct); return r is null?NotFound():Ok(r); }
    [HttpGet("/api/signatures/{id:guid}/download")][Authorize(Policy = "SignatureDownload")] public async Task<IActionResult> Download(Guid id,CancellationToken ct){ var tenantId=ReadGuidClaim("tenant_id"); if(tenantId is null) return Forbid(); var f=await packages.GenerateP7sAsync(tenantId.Value,id,ct); return File(f.Content,f.ContentType,f.FileName); }
    [HttpGet("/api/signatures/{id:guid}/package")][Authorize(Policy = "SignatureDownload")] public async Task<IActionResult> Package(Guid id,CancellationToken ct){ var tenantId=ReadGuidClaim("tenant_id"); if(tenantId is null) return Forbid(); var f=await packages.GenerateZipAsync(tenantId.Value,id,ct); return File(f.Content,f.ContentType,f.FileName); }
    private string BuildPublicContentUrl(Guid sessionId)
    {
        var baseUrl = options.Value.PublicServerBaseUrl?.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("DigitalSignature:PublicServerBaseUrl deve ser URL HTTPS absoluta.");
        return $"{uri.GetLeftPart(UriPartial.Authority)}/api/signing/sessions/{sessionId}/content";
    }
    private Guid? ReadGuidClaim(string type) => Guid.TryParse(User.FindFirst(type)?.Value, out var value) ? value : null;
    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool TryDecode(string input, int max, out byte[] bytes, out string? error){ bytes=[]; error=null; try{ bytes=Convert.FromBase64String(input); if(bytes.Length==0||bytes.Length>max){ error="payload_size_invalid"; return false;} return true;} catch(FormatException){ error="payload_base64_invalid"; return false;} }
}
