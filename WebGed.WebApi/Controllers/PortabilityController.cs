using InovaGed.Application.Continuity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebGed.WebApi.Controllers;

[ApiController]
[Authorize(Policy = "PortabilityExport")]
[Route("api/v1/portability")]
public sealed class PortabilityController(IPortabilityExportService exports, IPortabilityManifestService manifests, IAdministrativeTenantScopeResolver tenantScope) : ControllerBase
{
    [HttpPost("exports")]
    public async Task<IActionResult> Create([FromBody] CreatePortabilityExportRequest request, [FromHeader(Name="Idempotency-Key")] string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return Problem("Idempotency-Key é obrigatório.", statusCode: StatusCodes.Status400BadRequest);
        var scope = ResolveTenant(request.TenantId);
        if (!scope.Allowed) return Denied(scope.DenialReason);
        var result = await exports.RequestAsync(scope.TenantId, request.Scope, User.Identity?.Name ?? "api", key, HttpContext.TraceIdentifier, ct);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("exports/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var scope = ResolveTenant(tenantId);
        if (!scope.Allowed) return NotFound();
        return (await exports.GetAsync(id, scope.TenantId, ct)) is { } e ? Ok(e) : NotFound();
    }

    [HttpGet("exports/{id:guid}/manifest")]
    public async Task<IActionResult> Manifest(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var scope = ResolveTenant(tenantId);
        if (!scope.Allowed) return NotFound();
        var export = await exports.GetAsync(id, scope.TenantId, ct);
        if (export is null) return NotFound();
        if (!string.Equals(export.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase)) return Problem("Manifesto disponível somente para pacote AVAILABLE.", statusCode: StatusCodes.Status409Conflict);
        if (export.ExpiresAtUtc.HasValue && export.ExpiresAtUtc.Value <= DateTime.UtcNow) return NotFound();
        return Ok(await manifests.BuildAsync(id, ct));
    }

    [Authorize(Policy = "PortabilityDownload")]
    [HttpGet("exports/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var scope = ResolveTenant(tenantId);
        if (!scope.Allowed) return NotFound();
        var export = await exports.GetAsync(id, scope.TenantId, ct);
        if (export is null || (export.ExpiresAtUtc.HasValue && export.ExpiresAtUtc.Value <= DateTime.UtcNow)) return NotFound();
        if (!string.Equals(export.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase)) return Problem("Download disponível somente para pacote AVAILABLE.", statusCode: StatusCodes.Status409Conflict);
        Response.Headers.CacheControl = "no-store";
        return Problem("Download protegido exige artefato disponível, hash válido e caminho operacional protegido; caminho físico não é exposto.", statusCode: StatusCodes.Status409Conflict);
    }

    [HttpPost("exports/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var scope = ResolveTenant(tenantId);
        if (!scope.Allowed) return NotFound();
        return await exports.CancelAsync(id, scope.TenantId, User.Identity?.Name ?? "api", ct) ? NoContent() : NotFound();
    }

    [HttpGet("schema")]
    public IActionResult Schema() => Ok(new { formatVersion = "1.0", packageRoot = "inovaged-export", manifest = "manifest.json", checksums = "checksums.sha256" });

    private AdministrativeTenantScope ResolveTenant(Guid? requestedTenantId) => tenantScope.Resolve(User, requestedTenantId);

    private ObjectResult Denied(string? reason)
    {
        var problem = Problem(
            title: "Acesso negado",
            detail: reason ?? "Operação não autorizada para o tenant informado.",
            statusCode: StatusCodes.Status403Forbidden);
        if (problem.Value is ProblemDetails details) details.Extensions["correlationId"] = HttpContext.TraceIdentifier;
        return problem;
    }
}
public sealed record CreatePortabilityExportRequest(Guid? TenantId, string Scope);
