using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.GedAccess)]
public sealed class ProtocoloController : GedControllerBase
{
    private const long MaxFileSizeBytes = 25 * 1024 * 1024;
    private static readonly HashSet<string> ExtensoesBloqueadas = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".js", ".msi", ".dll" };
    private readonly InovaGed.Application.Ged.Loans.IProtocolAccessService _protocolAccess;
    private readonly IProtocoloCentralService _central;
    private readonly InovaGed.Application.Protocolo.IProtocolAiAssistService _aiAssist;

    public ProtocoloController(IDbConnectionFactory dbFactory, InovaGed.Application.Ged.Loans.IProtocolAccessService protocolAccess, IProtocoloCentralService central, InovaGed.Application.Protocolo.IProtocolAiAssistService aiAssist) : base(dbFactory)
    {
        _protocolAccess = protocolAccess;
        _central = central;
        _aiAssist = aiAssist;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? status, string? visao, string? tipo = null, Guid? setorId = null, DateTime? de = null, DateTime? ate = null, bool prazoVencido = false, int page = 1, int pageSize = 20, Guid? sel = null)
    {
        if (UserId is null) return Challenge();
        var result = await _central.SearchAsync(new ProtocoloCentralQuery
        {
            TenantId = TenantId,
            UserId = UserId.Value,
            CanSeeAll = IsAdminOrGestor(),
            Visao = string.IsNullOrWhiteSpace(visao) ? "a_receber" : visao,
            Q = q,
            Status = status,
            Tipo = tipo,
            SetorId = setorId,
            De = de,
            Ate = ate,
            PrazoVencido = prazoVencido,
            Page = page,
            PageSize = pageSize,
            SelecionadoId = sel
        }, HttpContext.RequestAborted);
        return View(result);
    }

    [Authorize(Policy = AppPolicies.ProtocolRequest)]
    [HttpGet]
    public async Task<IActionResult> Novo() { using var db = await OpenAsync(); var vm = new ProtocoloNovoVM(); await PopularCombosAsync(db, vm); return View(vm); }

    [Authorize(Policy = AppPolicies.ProtocolRequest)]
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(150_000_000)]
    public async Task<IActionResult> Novo(ProtocoloNovoVM vm, List<IFormFile>? arquivos)
    {
        using var db = await OpenAsync(); await PopularCombosAsync(db, vm); if (!ModelState.IsValid) return View(vm);
        if (!IsAdminOrGestor() && !User.IsInNormalizedRole(AppRoles.ArquivistaOphir) && !(await GetSetoresUsuarioAsync(db)).Any(x => x.SetorId == vm.SetorOrigemId)) { ModelState.AddModelError("", "Usuário não vinculado ao setor de origem."); return View(vm); }
        var erros = ValidarArquivos(arquivos); if (erros.Any()) { foreach (var e in erros) ModelState.AddModelError("", e); return View(vm); }
        using var tx = db.BeginTransaction();
        try
        {
            var num = await db.QuerySingleAsync<NumeroGerado>("select sequencial,numero from ged.protocolo_gerar_numero(@TenantId)", new { TenantId }, tx);
            var id = Guid.NewGuid(); var now = DateTime.Now; var status = vm.SalvarComoRascunho ? "RASCUNHO" : "ABERTO";
            var prioridade = await GetNomeCadastroAsync(db, "ged.protocolo_prioridade", vm.PrioridadeId) ?? "NORMAL";
            await db.ExecuteAsync(@"insert into ged.protocolo(id,tenant_id,numero,ano,sequencial,especie,tipo_solicitacao,procedencia,origem_pedido,assunto,descricao,informacoes_complementares,interessado,cpf_cnpj,email,telefone,solicitante_nome,solicitante_matricula,solicitante_cargo,prioridade,status,tipo_protocolo_id,assunto_id,prioridade_id,canal_entrada_id,setor_origem_id,setor_atual_id,setor_destino_inicial_id,criado_por,criado_por_nome,data_abertura,created_at,reg_status) values(@Id,@TenantId,@Numero,extract(year from now())::int,@Sequencial,@Especie,@TipoSolicitacao,@Procedencia,@OrigemPedido,@Assunto,@Descricao,@InformacoesComplementares,@Interessado,@CpfCnpj,@Email,@Telefone,@SolicitanteNome,@SolicitanteMatricula,@SolicitanteCargo,@Prioridade,@Status,@TipoProtocoloId,@AssuntoId,@PrioridadeId,@CanalEntradaId,@SetorOrigemId,@SetorAtualId,@SetorDestinoId,@UserId,@UserName,@DataAbertura,@Now,'A')", new { Id = id, TenantId, num.Numero, num.Sequencial, vm.Especie, vm.TipoSolicitacao, vm.Procedencia, vm.OrigemPedido, vm.Assunto, vm.Descricao, vm.InformacoesComplementares, vm.Interessado, vm.CpfCnpj, vm.Email, vm.Telefone, vm.SolicitanteNome, vm.SolicitanteMatricula, vm.SolicitanteCargo, Prioridade = prioridade, Status = status, vm.TipoProtocoloId, vm.AssuntoId, vm.PrioridadeId, vm.CanalEntradaId, vm.SetorOrigemId, SetorAtualId = vm.SetorOrigemId, vm.SetorDestinoId, UserId, UserName = UserNameSafe, DataAbertura = vm.SalvarComoRascunho ? (DateTime?)null : now, Now = now }, tx);
            await UpsertParticipanteAsync(db, tx, id, vm.SetorOrigemId, true, vm.SetorOrigemId == vm.SetorDestinoId);
            await UpsertParticipanteAsync(db, tx, id, vm.SetorDestinoId, true, !vm.SalvarComoRascunho);
            await RegistrarTramitacaoAsync(db, tx, id, vm.SetorOrigemId, vm.SetorDestinoId, "CRIACAO", null, status, "Protocolo criado.", null, null);
            await SalvarArquivosAsync(db, tx, id, vm.SetorOrigemId, await GetSetorNomeAsync(db, vm.SetorOrigemId) ?? "", arquivos, null, null);
            tx.Commit();
            if (!vm.SalvarComoRascunho && vm.SetorOrigemId != vm.SetorDestinoId && UserId is not null)
            {
                var fwd = await _central.ForwardAsync(Actor(), id, null, vm.SetorDestinoId, "Abertura do protocolo", null, $"novo:{id:N}", null, null, null, HttpContext.RequestAborted);
                TempData[fwd.Success ? "ok" : "erro"] = fwd.Success
                    ? $"Protocolo {num.Numero} criado. {fwd.Message}"
                    : $"Protocolo {num.Numero} foi criado no setor de origem, mas o encaminhamento não foi concluído: {fwd.Message}";
            }
            else TempData["ok"] = $"Protocolo {num.Numero} criado com sucesso.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch { tx.Rollback(); throw; }
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        using var db = await OpenAsync(); if (!await PodeVisualizarAsync(db, id)) { TempData["erro"] = "Sem permissão."; return RedirectToAction(nameof(Index)); }
        var vm = await db.QuerySingleOrDefaultAsync<ProtocoloDetailsVM>(@"select p.id Id,p.numero Numero,p.created_at CreatedAt,p.data_abertura DataAbertura,p.data_finalizacao DataFinalizacao,p.data_encerramento DataEncerramento,p.data_arquivamento DataArquivamento,p.especie Especie,p.tipo_solicitacao TipoSolicitacao,p.procedencia Procedencia,p.origem_pedido OrigemPedido,ce.nome CanalEntrada,p.assunto Assunto,p.descricao Descricao,p.informacoes_complementares InformacoesComplementares,p.interessado Interessado,p.cpf_cnpj CpfCnpj,p.email Email,p.telefone Telefone,p.solicitante_nome SolicitanteNome,p.solicitante_matricula SolicitanteMatricula,p.solicitante_cargo SolicitanteCargo,p.status Status,p.prioridade Prioridade,p.setor_origem_id SetorOrigemId,so.nome SetorOrigem,p.setor_atual_id SetorAtualId,sa.nome SetorAtual,p.criado_por_nome CriadoPorNome,p.justificativa_finalizacao JustificativaFinalizacao,p.justificativa_encerramento JustificativaEncerramento,p.justificativa_arquivamento JustificativaArquivamento from ged.protocolo p left join ged.protocolo_setor so on so.id=p.setor_origem_id left join ged.protocolo_setor sa on sa.id=p.setor_atual_id left join ged.protocolo_canal_entrada ce on ce.id=p.canal_entrada_id where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A'", new { TenantId, Id = id });
        if (vm == null) return NotFound(); var setorOk = await GetSetorOperacaoAsync(db, vm.SetorAtualId); var fechado = StatusEncerrado(vm.Status); vm.PodeOperar = setorOk.HasValue && !fechado; vm.PodeAnexar = vm.PodeOperar; vm.PodeTramitar = vm.PodeOperar; vm.PodeFinalizar = vm.PodeOperar; vm.PodeArquivar = vm.PodeOperar || IsAdminOrGestor(); vm.SetoresDestino = await GetSetoresSelectAsync(db); vm.TiposDocumento = await GetSelectAsync(db, "ged.protocolo_tipo_documento");
        var meusSetores = (await GetSetoresUsuarioAsync(db)).Select(x => x.SetorId).ToHashSet();
        vm.Documentos = (await db.QueryAsync<ProtocoloDocumentoVM>("select id Id,nome_arquivo NomeArquivo,content_type ContentType,tamanho_bytes TamanhoBytes,tipo_documento TipoDocumento,descricao Descricao,anexado_por_nome AnexadoPorNome,setor_nome SetorNome,setor_id SetorId,created_at CreatedAt from ged.protocolo_documento where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A' order by created_at desc", new { TenantId, Id = id })).ToList();
        foreach (var d in vm.Documentos) d.PodeExcluir = !fechado && d.SetorId.HasValue && meusSetores.Contains(d.SetorId.Value);
        vm.Tramitacoes = (await db.QueryAsync<ProtocoloTramitacaoVM>("select id Id,setor_origem_nome SetorOrigemNome,setor_destino_nome SetorDestinoNome,usuario_nome UsuarioNome,acao Acao,status_anterior StatusAnterior,status_novo StatusNovo,despacho Despacho,observacao Observacao,justificativa Justificativa,data_tramitacao DataTramitacao from ged.protocolo_tramitacao where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A' order by data_tramitacao desc", new { TenantId, Id = id })).ToList();
        vm.Observacoes = (await db.QueryAsync<ProtocoloObservacaoVM>("select id Id,setor_nome SetorNome,usuario_nome UsuarioNome,tipo Tipo,observacao Observacao,created_at CreatedAt from ged.protocolo_observacao where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A' and (tipo <> 'INTERNA_SETOR' or setor_id=any(@MeusSetores) or @Admin=true) order by created_at desc", new { TenantId, Id = id, MeusSetores = meusSetores.ToArray(), Admin = IsAdminOrGestor() })).ToList();
        if (UserId is Guid uid)
        {
            var central = await _central.SearchAsync(new ProtocoloCentralQuery { TenantId = TenantId, UserId = uid, CanSeeAll = IsAdminOrGestor(), Visao = "historico", SelecionadoId = id, PageSize = 1 }, HttpContext.RequestAborted);
            vm.Custodia = central.Selecionado;
            vm.CustodiaErro = central.Error;
            if (vm.Custodia is not null)
            {
                vm.PodeTramitar = vm.PodeTramitar || vm.Custodia.PodeTramitar;
                vm.PodeOperar = vm.PodeOperar || vm.Custodia.PodeReceber || vm.Custodia.PodeDevolver || vm.Custodia.PodeEstornar || vm.Custodia.PodeConfirmarRetorno || vm.Custodia.PodeTramitar;
            }
        }
        return View(vm);
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(150_000_000)] public async Task<IActionResult> Anexar(Guid protocoloId, Guid? tipoDocumentoId, string? descricao, List<IFormFile>? arquivos) { using var db = await OpenAsync(); var p = await GetBasicoAsync(db, protocoloId); if (p == null) return NotFound(); var setor = await GetSetorOperacaoAsync(db, p.SetorAtualId); if (!setor.HasValue || StatusEncerrado(p.Status)) { TempData["erro"] = "Sem permissão."; return RedirectToAction(nameof(Details), new { id = protocoloId }); } var erros = ValidarArquivos(arquivos); if (erros.Any()) { TempData["erro"] = string.Join(" ", erros); return RedirectToAction(nameof(Details), new { id = protocoloId }); } using var tx = db.BeginTransaction(); try { await SalvarArquivosAsync(db, tx, protocoloId, setor.Value, await GetSetorNomeAsync(db, setor.Value) ?? "", arquivos, tipoDocumentoId, descricao); await RegistrarTramitacaoAsync(db, tx, protocoloId, setor, p.SetorAtualId, "ANEXO", p.Status, p.Status, "Arquivo(s) anexado(s).", descricao, null); tx.Commit(); TempData["ok"] = "Arquivo(s) anexado(s)."; } catch { tx.Rollback(); throw; } return RedirectToAction(nameof(Details), new { id = protocoloId }); }
    [HttpGet] public async Task<IActionResult> VisualizarDocumento(Guid id) { using var db = await OpenAsync(); var d = await db.QuerySingleOrDefaultAsync<DocumentoArquivo>("select id Id,protocolo_id ProtocoloId,nome_arquivo NomeArquivo,content_type ContentType,arquivo_bytes ArquivoBytes from ged.protocolo_documento where tenant_id=@TenantId and id=@Id and reg_status='A'", new { TenantId, Id = id }); if (d == null || d.ArquivoBytes == null) return NotFound(); if (!await PodeVisualizarAsync(db, d.ProtocoloId)) return Forbid(); return File(d.ArquivoBytes, d.ContentType ?? "application/octet-stream", d.NomeArquivo); }
    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> ExcluirDocumento(Guid id, string? motivo) { using var db = await OpenAsync(); var d = await db.QuerySingleOrDefaultAsync<DocumentoBasico>("select d.id Id,d.protocolo_id ProtocoloId,d.setor_id SetorId,p.status Status,p.setor_atual_id SetorAtualId from ged.protocolo_documento d join ged.protocolo p on p.id=d.protocolo_id and p.tenant_id=d.tenant_id where d.tenant_id=@TenantId and d.id=@Id and d.reg_status='A'", new { TenantId, Id = id }); if (d == null) return NotFound(); var pode = d.SetorId.HasValue && (await GetSetoresUsuarioAsync(db)).Any(x => x.SetorId == d.SetorId.Value) && !StatusEncerrado(d.Status); if (!pode) { TempData["erro"] = "Somente o setor que anexou pode excluir."; return RedirectToAction(nameof(Details), new { id = d.ProtocoloId }); } await db.ExecuteAsync("update ged.protocolo_documento set reg_status='E',excluido_por=@UserId,excluido_por_nome=@UserName,excluido_at=now(),motivo_exclusao=@Motivo where tenant_id=@TenantId and id=@Id", new { TenantId, Id = id, UserId, UserName = UserNameSafe, Motivo = motivo ?? "Exclusão pelo setor responsável" }); return RedirectToAction(nameof(Details), new { id = d.ProtocoloId }); }
    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Tramitar(Guid protocoloId, Guid setorDestinoId, string? despacho, string? observacao)
    {
        var result = await _central.ForwardAsync(Actor(), protocoloId, null, setorDestinoId, despacho, observacao, null, null, null, null, HttpContext.RequestAborted);
        TempData[result.Success ? "ok" : "erro"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Receber(Guid protocoloId, Guid? movimentoId)
    {
        var result = await _central.ReceiveAsync(Actor(), movimentoId, protocoloId, HttpContext.RequestAborted);
        TempData[result.Success ? "ok" : "erro"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Devolver(Guid protocoloId, string? observacao)
    {
        var result = await _central.ReturnAsync(Actor(), protocoloId, observacao, null, HttpContext.RequestAborted);
        TempData[result.Success ? "ok" : "erro"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarRetorno(Guid protocoloId, Guid? movimentoId)
    {
        var result = await _central.ConfirmReturnAsync(Actor(), movimentoId, protocoloId, HttpContext.RequestAborted);
        TempData[result.Success ? "ok" : "erro"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Estornar(Guid protocoloId, string justificativa)
    {
        var result = await _central.ReverseAsync(Actor(), protocoloId, justificativa, HttpContext.RequestAborted);
        TempData[result.Success ? "ok" : "erro"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reabrir(Guid protocoloId, string justificativa)
    {
        var result = await _central.ReopenAsync(Actor(), protocoloId, justificativa, HttpContext.RequestAborted);
        TempData[result.Success ? "ok" : "erro"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Lote(string acao, Guid[] ids, Guid? destinoSetorId, string? observacao, string? confirmar)
    {
        var command = new ProtocoloLoteCommand { Acao = acao ?? "", Ids = ids ?? [], DestinoSetorId = destinoSetorId, Observacao = observacao, IdempotencyKey = confirmar == "1" ? Guid.NewGuid().ToString("N") : null };
        if (confirmar != "1")
        {
            var preview = await _central.PreviewBatchAsync(Actor(), command, HttpContext.RequestAborted);
            return View("Lote", preview);
        }
        IReadOnlyList<ProtocoloBatchItemResult> itens;
        try
        {
            itens = await _central.ExecuteBatchAsync(Actor(), command, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            var atendimento = Guid.NewGuid().ToString("N")[..12];
            // Detalhe técnico fica no log do host; a tela recebe só o identificador.
            Console.Error.WriteLine($"Lote protocolo atendimento {atendimento}: {ex.GetType().Name}");
            TempData["erro"] = $"O lote não foi concluído. Atendimento: {atendimento}.";
            return RedirectToAction(nameof(Index));
        }
        var aplicados = itens.Count(i => i.Aplicado);
        TempData[aplicados == itens.Count && aplicados > 0 ? "ok" : "erro"] = aplicados == 0
            ? "Nenhum item do lote foi aplicado."
            : aplicados == itens.Count ? $"{aplicados} item(ns) processados." : $"Processamento parcial: {aplicados} de {itens.Count}. Os demais permanecem pendentes.";
        return View("Lote", new ProtocoloBatchPreview { Acao = acao ?? "", AcaoLabel = "Resultado", Observacao = observacao, Itens = itens.ToList() });
    }

    [HttpGet]
    public async Task<IActionResult> Exportar(string? q, string? status, string? visao, string? tipo = null, Guid? setorId = null, DateTime? de = null, DateTime? ate = null, bool prazoVencido = false, int page = 1)
    {
        if (UserId is null) return Challenge();
        var result = await _central.SearchAsync(new ProtocoloCentralQuery
        {
            TenantId = TenantId, UserId = UserId.Value, CanSeeAll = IsAdminOrGestor(),
            Visao = visao ?? "a_receber", Q = q, Status = status, Tipo = tipo, SetorId = setorId, De = de, Ate = ate, PrazoVencido = prazoVencido, Page = page, PageSize = 100
        }, HttpContext.RequestAborted);
        var sb = new StringBuilder();
        sb.AppendLine("numero;codigo;tipo;interessado;assunto;situacao;setor;responsavel;ultima_movimentacao;prazo");
        foreach (var row in result.Rows)
        {
            sb.Append(Csv(row.Numero)).Append(';').Append(Csv(row.CodigoDocumental)).Append(';').Append(Csv(row.Tipo)).Append(';')
                .Append(Csv(row.Interessado)).Append(';').Append(Csv(row.Assunto)).Append(';').Append(Csv(row.SituacaoLabel)).Append(';')
                .Append(Csv(row.Setor)).Append(';').Append(Csv(row.Responsavel)).Append(';')
                .Append(Csv(row.UltimaMovimentacao?.ToString("yyyy-MM-dd HH:mm"))).Append(';').Append(Csv(row.Prazo?.ToString("yyyy-MM-dd"))).AppendLine();
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "tramitacao.csv");
    }
    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> AdicionarObservacao(Guid protocoloId, string tipo, string observacao) { using var db = await OpenAsync(); var p = await GetBasicoAsync(db, protocoloId); if (p == null) return NotFound(); var setor = await GetSetorOperacaoAsync(db, p.SetorAtualId); if (!setor.HasValue || StatusEncerrado(p.Status)) return RedirectToAction(nameof(Details), new { id = protocoloId }); await db.ExecuteAsync("insert into ged.protocolo_observacao(tenant_id,protocolo_id,setor_id,setor_nome,usuario_id,usuario_nome,tipo,observacao) values(@TenantId,@Id,@SetorId,@SetorNome,@UserId,@UserName,@Tipo,@Obs)", new { TenantId, Id = protocoloId, SetorId = setor.Value, SetorNome = await GetSetorNomeAsync(db, setor.Value), UserId, UserName = UserNameSafe, Tipo = string.IsNullOrWhiteSpace(tipo) ? "PUBLICA" : tipo, Obs = observacao }); return RedirectToAction(nameof(Details), new { id = protocoloId }); }
    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Deferir(Guid protocoloId, string justificativa) => Encerrar(protocoloId, "DEFERIDO", "DEFERIMENTO", justificativa);
    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Indeferir(Guid protocoloId, string justificativa) => Encerrar(protocoloId, "INDEFERIDO", "INDEFERIMENTO", justificativa);
    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Finalizar(Guid protocoloId, string justificativa) => Encerrar(protocoloId, "FINALIZADO", "FINALIZACAO", justificativa);
    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Arquivar(Guid protocoloId, string justificativa) => Encerrar(protocoloId, "ARQUIVADO", "ARQUIVAMENTO", justificativa);
    private async Task<IActionResult> Encerrar(Guid id, string status, string acao, string just)
    {
        using var db = await OpenAsync();
        using var tx = db.BeginTransaction();
        var p = await db.QuerySingleOrDefaultAsync<Basico>("select id Id,status Status,setor_atual_id SetorAtualId from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A' for update", new { TenantId, Id = id }, tx);
        if (p == null) return NotFound();
        if (string.IsNullOrWhiteSpace(just)) { TempData["erro"] = "Informe justificativa."; return RedirectToAction(nameof(Details), new { id }); }
        var setor = await GetSetorOperacaoAsync(db, p.SetorAtualId);
        if (!setor.HasValue && !IsAdminOrGestor()) return Forbid();
        var pendente = await db.ExecuteScalarAsync<int>("select count(*) from ged.protocolo_tramitacao where tenant_id=@TenantId and protocolo_id=@Id and reg_status='A' and ativa=true and situacao_movimentacao in ('AGUARDANDO_RECEBIMENTO','DEVOLUCAO_PENDENTE')", new { TenantId, Id = id }, tx);
        if (pendente > 0) { TempData["erro"] = "Há movimentação pendente. Receba, confirme o retorno ou estorne antes de encerrar. Arquivar o protocolo não elimina o documento nem cumpre a temporalidade."; return RedirectToAction(nameof(Details), new { id }); }
        await db.ExecuteAsync("update ged.protocolo set status=@Status, situacao_custodia='ENCERRADO', data_encerramento=now(),justificativa_encerramento=@Just,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id", new { TenantId, Id = id, Status = status, Just = just, UserId }, tx);
        await db.ExecuteAsync("insert into ged.protocolo_tramitacao(tenant_id,protocolo_id,setor_origem_id,setor_destino_id,usuario_id,usuario_nome,acao,status_anterior,status_novo,justificativa,ativa) values(@TenantId,@Id,@Setor,@Setor,@UserId,@UserName,@Acao,@Ant,@Novo,@Just,false)", new { TenantId, Id = id, Setor = p.SetorAtualId, UserId, UserName = UserNameSafe, Acao = acao, Ant = p.Status, Novo = status, Just = just }, tx);
        tx.Commit();
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopularCombosAsync(IDbConnection db, ProtocoloNovoVM vm) { vm.Setores = await GetSetoresSelectAsync(db); vm.Tipos = await GetSelectAsync(db, "ged.protocolo_tipo"); vm.Assuntos = await GetSelectAsync(db, "ged.protocolo_assunto"); vm.Prioridades = await GetSelectAsync(db, "ged.protocolo_prioridade"); vm.CanaisEntrada = await GetSelectAsync(db, "ged.protocolo_canal_entrada"); }
    private async Task<List<SelectListItem>> GetSetoresSelectAsync(IDbConnection db) => (await db.QueryAsync<(Guid Id, string Nome)>("select id,nome from ged.protocolo_setor where tenant_id=@TenantId and reg_status='A' and ativo=true order by ordem,nome", new { TenantId })).Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nome }).ToList();
    private async Task<List<SelectListItem>> GetSelectAsync(IDbConnection db, string tabela)
{
    var sql = $@"
select 
    id::text as ""Value"", 
    nome as ""Text""
from {tabela}
where tenant_id = @TenantId
  and coalesce(reg_status, 'A') = 'A'
  and coalesce(ativo, true) = true
order by coalesce(ordem, 0), nome;";

    var rows = await db.QueryAsync(sql, new { TenantId });

    return rows
        .Select(x => new SelectListItem
        {
            Value = (string)x.Value,
            Text = (string)x.Text
        })
        .ToList();
}
    private async Task<string?> GetNomeCadastroAsync(IDbConnection db, string tabela, Guid? id) => id.HasValue ? await db.ExecuteScalarAsync<string?>($"select nome from {tabela} where tenant_id=@TenantId and id=@Id", new { TenantId, Id = id.Value }) : null;
    private async Task<List<SetorUsuarioVM>> GetSetoresUsuarioAsync(IDbConnection db) => UserId == null ? new() : (await db.QueryAsync<SetorUsuarioVM>("select s.id SetorId,s.nome Nome,s.sigla Sigla from ged.protocolo_usuario_setor us join ged.protocolo_setor s on s.id=us.setor_id where us.tenant_id=@TenantId and us.usuario_id=@UserId and us.reg_status='A' and us.ativo=true", new { TenantId, UserId })).ToList();
    private async Task<Guid?> GetSetorOperacaoAsync(IDbConnection db, Guid? setorAtualId) { if (!setorAtualId.HasValue) return null; if (IsAdminOrGestor()) return setorAtualId; return (await GetSetoresUsuarioAsync(db)).Any(x => x.SetorId == setorAtualId) ? setorAtualId : null; }
    private Task<bool> PodeVisualizarAsync(IDbConnection db, Guid id) => _protocolAccess.CanViewProtocolAsync(TenantId, id, UserId, User, HttpContext.RequestAborted);
    private async Task<Basico?> GetBasicoAsync(IDbConnection db, Guid id) => await db.QuerySingleOrDefaultAsync<Basico>("select id Id,status Status,setor_atual_id SetorAtualId from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A'", new { TenantId, Id = id });
    private async Task<string?> GetSetorNomeAsync(IDbConnection db, Guid id) => await db.ExecuteScalarAsync<string?>("select nome from ged.protocolo_setor where tenant_id=@TenantId and id=@Id", new { TenantId, Id = id });
    private async Task UpsertParticipanteAsync(IDbConnection db, IDbTransaction tx, Guid pid, Guid setor, bool ver, bool editar) => await db.ExecuteAsync("insert into ged.protocolo_setor_participante(tenant_id,protocolo_id,setor_id,pode_visualizar,pode_editar) values(@TenantId,@Pid,@Setor,@Ver,@Editar) on conflict(tenant_id,protocolo_id,setor_id) do update set pode_visualizar=excluded.pode_visualizar,pode_editar=excluded.pode_editar,participou_em=now()", new { TenantId, Pid = pid, Setor = setor, Ver = ver, Editar = editar }, tx);
    private async Task RegistrarTramitacaoAsync(IDbConnection db, IDbTransaction tx, Guid pid, Guid? origem, Guid? destino, string acao, string? ant, string? novo, string? despacho, string? obs, string? just) => await db.ExecuteAsync("insert into ged.protocolo_tramitacao(tenant_id,protocolo_id,setor_origem_id,setor_origem_nome,setor_destino_id,setor_destino_nome,usuario_id,usuario_nome,acao,status_anterior,status_novo,despacho,observacao,justificativa,ip,user_agent) values(@TenantId,@Pid,@Origem,@OrigemNome,@Destino,@DestinoNome,@UserId,@UserName,@Acao,@Ant,@Novo,@Despacho,@Obs,@Just,@Ip,@Ua)", new { TenantId, Pid = pid, Origem = origem, OrigemNome = origem.HasValue ? await GetSetorNomeAsync(db, origem.Value) : null, Destino = destino, DestinoNome = destino.HasValue ? await GetSetorNomeAsync(db, destino.Value) : null, UserId, UserName = UserNameSafe, Acao = acao, Ant = ant, Novo = novo, Despacho = despacho, Obs = obs, Just = just, Ip = HttpContext.Connection.RemoteIpAddress?.ToString(), Ua = Request.Headers.UserAgent.ToString() }, tx);
    private async Task SalvarArquivosAsync(IDbConnection db, IDbTransaction tx, Guid pid, Guid setor, string setorNome, List<IFormFile>? arqs, Guid? tipoId, string? desc) { if (arqs == null) return; var tipo = await GetNomeCadastroAsync(db, "ged.protocolo_tipo_documento", tipoId); foreach (var f in arqs.Where(x => x.Length > 0)) { await using var ms = new MemoryStream(); await f.CopyToAsync(ms); var b = ms.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant(); await db.ExecuteAsync("insert into ged.protocolo_documento(tenant_id,protocolo_id,nome_arquivo,content_type,tamanho_bytes,arquivo_bytes,hash_arquivo,tipo_documento_id,tipo_documento,descricao,anexado_por,anexado_por_nome,setor_id,setor_nome) values(@TenantId,@Pid,@Nome,@Content,@Tam,@Bytes,@Hash,@TipoId,@Tipo,@Desc,@UserId,@UserName,@Setor,@SetorNome)", new { TenantId, Pid = pid, Nome = Path.GetFileName(f.FileName), Content = f.ContentType, Tam = f.Length, Bytes = b, Hash = hash, TipoId = tipoId, Tipo = tipo, Desc = desc, UserId, UserName = UserNameSafe, Setor = setor, SetorNome = setorNome }, tx); } }
    private List<string> ValidarArquivos(List<IFormFile>? arqs) { var l = new List<string>(); if (arqs == null) return l; foreach (var f in arqs) { if (f.Length > MaxFileSizeBytes) l.Add($"{f.FileName} excede 25MB."); if (ExtensoesBloqueadas.Contains(Path.GetExtension(f.FileName))) l.Add($"{f.FileName} possui extensão bloqueada."); } return l; }
    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpGet("/Protocolo/WorkQueueLegacy")]
    public Task<IActionResult> WorkQueue(string? q, string? status) => Index(q, status, "entrada");

    private ProtocoloActor Actor() => new()
    {
        TenantId = TenantId,
        UserId = UserId ?? Guid.Empty,
        UserName = UserNameSafe ?? "",
        CanSeeAll = IsAdminOrGestor(),
        IsFullAdmin = RolePolicyHelper.IsFullAdmin(User),
        Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
        UserAgent = Request.Headers.UserAgent.ToString()
    };

    private static string Csv(string? value)
    {
        var text = value ?? "";
        return text.Contains(';') || text.Contains('"') || text.Contains('\n') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private bool IsAdminOrGestor() => RolePolicyHelper.IsFullAdmin(User) || User.IsInNormalizedRole(AppRoles.AdministradorOphir);
    private static bool StatusEncerrado(string? s) => new[] { "FINALIZADO", "ARQUIVADO", "CANCELADO", "DEFERIDO", "INDEFERIDO" }.Contains((s ?? "").ToUpperInvariant());
    private sealed class NumeroGerado { public int Sequencial { get; set; } public string Numero { get; set; } = ""; }
    private sealed class Basico { public Guid Id { get; set; } public string Status { get; set; } = ""; public Guid? SetorAtualId { get; set; } }
    private sealed class DocumentoBasico { public Guid Id { get; set; } public Guid ProtocoloId { get; set; } public Guid? SetorId { get; set; } public string Status { get; set; } = ""; public Guid? SetorAtualId { get; set; } }
    private sealed class DocumentoArquivo { public Guid Id { get; set; } public Guid ProtocoloId { get; set; } public string NomeArquivo { get; set; } = ""; public string? ContentType { get; set; } public byte[]? ArquivoBytes { get; set; } }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiAssist(Guid protocoloId, string? taskKind, string? idempotencyKey, CancellationToken ct)
    {
        try {
            var res = await _aiAssist.AssistAsync(new InovaGed.Application.Protocolo.ProtocolAiAssistRequest { ProtocoloId = protocoloId, TaskKind = taskKind, IdempotencyKey = idempotencyKey }, ct);
            return Json(res);
        } catch (UnauthorizedAccessException) { return StatusCode(403, new { success = false, message = "Acesso não autorizado ao protocolo ou documentos vinculados." }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { success = false, message = "Erro ao processar assistência de IA.", error = ex.Message }); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiApplySubject(Guid protocoloId, Guid executionId, long concurrencyToken, string subject, bool accepted = true, string? notes = null, CancellationToken ct = default)
    {
        try {
            var res = await _aiAssist.ApplySubjectAsync(new InovaGed.Application.Protocolo.ProtocolAiApplySubjectRequest { ProtocoloId = protocoloId, ExecutionId = executionId, ConcurrencyToken = concurrencyToken, Subject = subject, Accepted = accepted, Notes = notes }, ct);
            return Json(res);
        } catch (UnauthorizedAccessException) { return StatusCode(403, new { success = false, message = "Acesso não autorizado." }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { success = false, message = "Erro ao aplicar assunto.", error = ex.Message }); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiApplyDraft(Guid protocoloId, Guid executionId, long concurrencyToken, string draftText, bool accepted = true, string? notes = null, CancellationToken ct = default)
    {
        try {
            var res = await _aiAssist.ApplyDispatchDraftAsync(new InovaGed.Application.Protocolo.ProtocolAiApplyDraftRequest { ProtocoloId = protocoloId, ExecutionId = executionId, ConcurrencyToken = concurrencyToken, DraftText = draftText, Accepted = accepted, Notes = notes }, ct);
            return Json(res);
        } catch (UnauthorizedAccessException) { return StatusCode(403, new { success = false, message = "Acesso não autorizado." }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { success = false, message = "Erro ao aplicar minuta.", error = ex.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> AiHistory(Guid protocoloId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        try {
            var res = await _aiAssist.GetReviewHistoryAsync(protocoloId, page, pageSize, ct);
            return Json(res);
        } catch (UnauthorizedAccessException) { return StatusCode(403, new { success = false, message = "Acesso não autorizado." }); }
        catch (Exception ex) { return StatusCode(500, new { success = false, message = "Erro ao carregar histórico.", error = ex.Message }); }
    }
}
