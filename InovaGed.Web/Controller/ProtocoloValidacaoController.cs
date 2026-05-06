using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[AllowAnonymous]
[Route("ValidarProtocolo")]
public sealed class ProtocoloValidacaoController : Controller
{
    private readonly IDbConnectionFactory _dbFactory;
    public ProtocoloValidacaoController(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

    [HttpGet("{codigo:guid}")]
    public async Task<IActionResult> Index(Guid codigo)
    {
        using var db = _dbFactory.CreateConnection();
        if (db.State != System.Data.ConnectionState.Open) db.Open();

        var item = await db.QuerySingleOrDefaultAsync<ProtocoloValidacaoVM>(@"
select true as Encontrado, numero as Numero, assunto as Assunto, interessado as Interessado,
status as Status, created_at as CreatedAt, hash_comprovante as HashComprovante
from ged.protocolo
where codigo_validacao = @Codigo and reg_status = 'A';", new { Codigo = codigo });

        item ??= new ProtocoloValidacaoVM { Encontrado = false };
        return View("~/Views/ProtocoloValidacao/Index.cshtml", item);
    }
}
