using InovaGed.Application.Continuity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebGed.WebApi.Controllers;

[ApiController]
[Authorize(Policy = "PortabilityExport")]
[Route("api/v1/portability")]
public sealed class PortabilityController(IPortabilityExportService exports, IPortabilityManifestService manifests) : ControllerBase
{
    [HttpPost("exports")]
    public async Task<IActionResult> Create([FromBody] CreatePortabilityExportRequest request, [FromHeader(Name="Idempotency-Key")] string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return Problem("Idempotency-Key é obrigatório.", statusCode: StatusCodes.Status400BadRequest);
        var result = await exports.RequestAsync(ResolveTenant(request.TenantId), request.Scope, User.Identity?.Name ?? "api", key, HttpContext.TraceIdentifier, ct);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpGet("exports/{id:guid}")] public async Task<IActionResult> Get(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct) => (await exports.GetAsync(id, ResolveTenant(tenantId), ct)) is { } e ? Ok(e) : NotFound();
    [HttpGet("exports/{id:guid}/manifest")] public async Task<IActionResult> Manifest(Guid id, CancellationToken ct) => Ok(await manifests.BuildAsync(id, ct));
    [Authorize(Policy = "PortabilityDownload")]
    [HttpGet("exports/{id:guid}/download")] public IActionResult Download(Guid id) { Response.Headers.CacheControl = "no-store"; return Problem("Download protegido exige artefato disponível e validação de tenant; dump de banco não é exposto.", statusCode: StatusCodes.Status409Conflict); }
    [HttpPost("exports/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct) => await exports.CancelAsync(id, ResolveTenant(tenantId), User.Identity?.Name ?? "api", ct) ? NoContent() : NotFound();
    [HttpGet("schema")] public IActionResult Schema() => Ok(new { formatVersion = "1.0", packageRoot = "inovaged-export", manifest = "manifest.json", checksums = "checksums.sha256" });

    private Guid? ResolveTenant(Guid? requestedTenantId)
    {
        var isGlobal = User.IsInRole("ADMIN") || User.HasClaim("scope", "global-tenants");
        var tenantClaim = User.FindFirst("tenant_id")?.Value;
        var currentTenant = Guid.TryParse(tenantClaim, out var parsed) ? parsed : (Guid?)null;
        if (!isGlobal && requestedTenantId.HasValue && currentTenant.HasValue && requestedTenantId != currentTenant)
            throw new UnauthorizedAccessException("Tenant informado não pertence ao usuário autenticado.");
        return isGlobal ? requestedTenantId ?? currentTenant : currentTenant ?? requestedTenantId;
    }
}
public sealed record CreatePortabilityExportRequest(Guid? TenantId, string Scope);
