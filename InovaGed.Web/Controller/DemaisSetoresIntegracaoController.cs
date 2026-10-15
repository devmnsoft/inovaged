using InovaGed.Application.Sectors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/integracoes/demais-setores")]
public sealed class DemaisSetoresIntegracaoController : ControllerBase
{
    private readonly ISectorOpeningService _opening;

    public DemaisSetoresIntegracaoController(ISectorOpeningService opening) => _opening = opening;

    [HttpPost("aberturas")]
    public async Task<IActionResult> Aberturas([FromBody] AberturaRequest? body, CancellationToken ct)
    {
        RestrictedHeaders();
        if (!SingleHeaders()) return BadRequest(new { codigo=SectorOpeningCodes.Invalid });
        body ??= new AberturaRequest();
        var opened = await _opening.OpenAsync(new SectorOpeningRequest(
            Request.Headers["X-InovaGed-Integracao"].ToString(),
            Request.Headers["X-InovaGed-Chave"].ToString(),
            body.Paciente?.TipoIdentificador,
            body.Paciente?.Identificador,
            body.SistemaOrigem,
            body.UnidadeOrigem,
            body.CodigoAtendimento,
            body.NomeDocumento,
            body.CodigoDocumento,
            body.DocumentoId,
            body.VersaoId,
            body.Modo,
            body.OrigemIncorporacao,
            Request.Headers["X-InovaGed-Idempotencia"].ToString()), ct);
        return OpeningResult(opened);
    }

    [HttpGet("aberturas/estado")]
    public async Task<IActionResult> Estado(CancellationToken ct)
    {
        RestrictedHeaders();
        if (!SingleHeaders()) return BadRequest(new { codigo=SectorOpeningCodes.Invalid });
        var state=await _opening.OpeningStateAsync(Request.Headers["X-InovaGed-Integracao"],Request.Headers["X-InovaGed-Chave"],Request.Headers["X-InovaGed-Idempotencia"],ct);
        var payload=new { codigo=state.Code,estado=state.State,correlacao=state.CorrelationId,validade=state.ExpiresAt,recuperacoes=state.Recoveries };
        if (!state.Authorized) return Unauthorized(payload);
        return Ok(payload);
    }

    [HttpPost("aberturas/recuperar")]
    public async Task<IActionResult> Recuperar(CancellationToken ct)
    {
        RestrictedHeaders();
        if (!SingleHeaders()) return BadRequest(new { codigo=SectorOpeningCodes.Invalid });
        return OpeningResult(await _opening.RecoverOpeningAsync(Request.Headers["X-InovaGed-Integracao"],Request.Headers["X-InovaGed-Chave"],Request.Headers["X-InovaGed-Idempotencia"],ct));
    }

    private bool SingleHeaders()=>new[] { "X-InovaGed-Integracao","X-InovaGed-Chave","X-InovaGed-Idempotencia" }.All(key=>Request.Headers[key].Count<=1);
    private void RestrictedHeaders()
    {
        Response.Headers.CacheControl="private, no-store";
        Response.Headers["Referrer-Policy"]="no-referrer";
    }
    private IActionResult OpeningResult(SectorOpeningResponse opened)
    {
        var payload = new
        {
            codigo = opened.Code,
            mensagem = opened.Message,
            urlAbertura = opened.PageUrl,
            urlIncorporacao = opened.FrameUrl,
            validade = opened.ExpiresAt,
            escopo = opened.Scope,
            correlacao = opened.CorrelationId,
            ocorrencias = opened.Occurrences
        };
        if (opened.Code == SectorOpeningCodes.Unauthorized)
            return Unauthorized(payload);
        if (opened.Code == SectorOpeningCodes.Conflict) return Conflict(payload);
        if (opened.Code == SectorOpeningCodes.RateLimited) return StatusCode(429,payload);
        if (opened.Accepted && opened.Code == SectorOpeningCodes.Repeated) return Accepted(payload);
        return opened.Accepted ? Ok(payload) : UnprocessableEntity(payload);
    }

    public sealed class AberturaRequest
    {
        public PacienteRequest? Paciente { get; set; }
        public string? SistemaOrigem { get; set; }
        public string? UnidadeOrigem { get; set; }
        public string? CodigoAtendimento { get; set; }
        public string? NomeDocumento { get; set; }
        public string? CodigoDocumento { get; set; }
        public Guid? DocumentoId { get; set; }
        public Guid? VersaoId { get; set; }
        public string? Modo { get; set; }
        public string? OrigemIncorporacao { get; set; }
    }

    public sealed class PacienteRequest
    {
        public string? TipoIdentificador { get; set; }
        public string? Identificador { get; set; }
    }
}
