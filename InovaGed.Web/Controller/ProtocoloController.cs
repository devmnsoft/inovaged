using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Retention;
using InovaGed.Application.Security;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using InovaGed.Web.Services;
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
    private readonly IRetentionJobRepository _retentionJobs;
    private readonly IAbacAuthorizationService _documentAuthorization;
    private readonly ILogger<ProtocoloController>? _logger;

    public ProtocoloController(IDbConnectionFactory dbFactory, InovaGed.Application.Ged.Loans.IProtocolAccessService protocolAccess, IProtocoloCentralService central, InovaGed.Application.Protocolo.IProtocolAiAssistService aiAssist, IRetentionJobRepository retentionJobs, IAbacAuthorizationService documentAuthorization, ILogger<ProtocoloController>? logger = null) : base(dbFactory)
    {
        _protocolAccess = protocolAccess;
        _central = central;
        _aiAssist = aiAssist;
        _retentionJobs = retentionJobs;
        _documentAuthorization = documentAuthorization;
        _logger = logger;
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
    public async Task<IActionResult> Novo(Guid? gedDocumentId)
    {
        using var db = await OpenAsync();
        var vm = new ProtocoloNovoVM { Id = Guid.NewGuid() };
        await PopularCombosAsync(db, vm);
        if (ProtocolOriginLinkRules.RequestsLink(gedDocumentId))
        {
            var docId = gedDocumentId!.Value;
            vm.GedDocumentId = docId;
            vm.GedLinkNotice = ProtocolOriginLinkRules.Requirement;
            if (UserId is Guid uid && await _documentAuthorization.CanAccessDocumentAsync(TenantId, uid, docId, "VIEW", new Dictionary<string, string>(), HttpContext.RequestAborted))
            {
                vm.GedDocumentTitle = await db.ExecuteScalarAsync<string?>("select coalesce(nullif(btrim(title),''), code) from ged.document where tenant_id=@TenantId and id=@Id and reg_status='A'", new { TenantId, Id = docId });
            }
        }
        return View(vm);
    }

    [Authorize(Policy = AppPolicies.ProtocolRequest)]
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(150_000_000)]
    public async Task<IActionResult> Novo(ProtocoloNovoVM vm, List<IFormFile>? arquivos)
    {
        using var db = await OpenAsync(); await PopularCombosAsync(db, vm); if (!ModelState.IsValid) return View(vm);
        if (!IsAdminOrGestor() && !User.IsInNormalizedRole(AppRoles.ArquivistaOphir) && !(await GetSetoresUsuarioAsync(db)).Any(x => x.SetorId == vm.SetorOrigemId)) { ModelState.AddModelError("", "Usuário não vinculado ao setor de origem."); return View(vm); }
        var erros = ValidarArquivos(arquivos); if (erros.Any()) { foreach (var e in erros) ModelState.AddModelError("", e); return View(vm); }
        var origemSolicitada = ProtocolOriginLinkRules.RequestsLink(vm.GedDocumentId);
        if (origemSolicitada)
        {
            var bloqueio = await GedOriginBlockReasonAsync(db, vm.GedDocumentId!.Value, null);
            if (bloqueio is not null)
            {
                ModelState.AddModelError(string.Empty, bloqueio);
                vm.GedLinkNotice = ProtocolOriginLinkRules.Requirement;
                return View(vm);
            }
        }
        var id = vm.Id.HasValue && vm.Id.Value != Guid.Empty ? vm.Id.Value : Guid.NewGuid();
        vm.Id = id;

        // Verificar se este protocolo já foi gravado em tentativa anterior (timeout/resubmissão)
        var existente = await db.QuerySingleOrDefaultAsync<ProtocoloResumoExistente>(
            @"select id as Id, numero as Numero, status as Status, setor_atual_id as SetorAtualId, 
                     setor_origem_id as SetorOrigemId, setor_destino_inicial_id as SetorDestinoInicialId 
              from ged.protocolo 
              where tenant_id = @TenantId and id = @Id and reg_status = 'A'",
            new { TenantId, Id = id });

        if (existente is not null)
        {
            _logger?.LogInformation("Protocolo {Numero} ({Id}) já persistido; retomando sem duplicar.", existente.Numero, id);
            var vinculoJaExiste = origemSolicitada ? " e vinculado ao documento de origem" : "";
            if (!vm.SalvarComoRascunho && existente.SetorOrigemId != vm.SetorDestinoId && UserId is not null)
            {
                if (existente.SetorAtualId == existente.SetorOrigemId)
                {
                    try
                    {
                        var fwd = await _central.ForwardAsync(Actor(), id, null, vm.SetorDestinoId, "Abertura do protocolo (retomada)", null, $"novo:{id:N}", null, null, null, HttpContext.RequestAborted);
                        TempData[fwd.Success ? "ok" : "erro"] = fwd.Success
                            ? $"Protocolo {existente.Numero} já havia sido criado{vinculoJaExiste} e foi encaminhado com sucesso. {fwd.Message}"
                            : $"Protocolo {existente.Numero} já está criado{vinculoJaExiste} no setor de origem, mas o encaminhamento permanece pendente: {fwd.Message}";
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Erro ao encaminhar protocolo {Numero} ({Id}) na retomada.", existente.Numero, id);
                        TempData["erro"] = $"Protocolo {existente.Numero} já está criado{vinculoJaExiste} no setor de origem, mas o encaminhamento permanece pendente devido a uma instabilidade temporária.";
                    }
                }
                else
                {
                    TempData["ok"] = $"Protocolo {existente.Numero} já havia sido criado{vinculoJaExiste} e encaminhado anteriormente.";
                }
            }
            else
            {
                TempData["ok"] = $"Protocolo {existente.Numero} já havia sido criado{vinculoJaExiste} com sucesso.";
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        NumeroGerado num;
        var committed = false;
        using var tx = db.BeginTransaction();
        try
        {
            num = await db.QuerySingleAsync<NumeroGerado>("select sequencial,numero from ged.protocolo_gerar_numero(@TenantId)", new { TenantId }, tx);
            var now = DateTime.Now; var status = vm.SalvarComoRascunho ? "RASCUNHO" : "ABERTO";
            var prioridade = await GetNomeCadastroAsync(db, "ged.protocolo_prioridade", vm.PrioridadeId) ?? "NORMAL";
            await db.ExecuteAsync(@"insert into ged.protocolo(id,tenant_id,numero,ano,sequencial,especie,tipo_solicitacao,procedencia,origem_pedido,assunto,descricao,informacoes_complementares,interessado,cpf_cnpj,email,telefone,solicitante_nome,solicitante_matricula,solicitante_cargo,prioridade,status,tipo_protocolo_id,assunto_id,prioridade_id,canal_entrada_id,setor_origem_id,setor_atual_id,setor_destino_inicial_id,criado_por,criado_por_nome,data_abertura,created_at,reg_status) values(@Id,@TenantId,@Numero,extract(year from now())::int,@Sequencial,@Especie,@TipoSolicitacao,@Procedencia,@OrigemPedido,@Assunto,@Descricao,@InformacoesComplementares,@Interessado,@CpfCnpj,@Email,@Telefone,@SolicitanteNome,@SolicitanteMatricula,@SolicitanteCargo,@Prioridade,@Status,@TipoProtocoloId,@AssuntoId,@PrioridadeId,@CanalEntradaId,@SetorOrigemId,@SetorAtualId,@SetorDestinoId,@UserId,@UserName,@DataAbertura,@Now,'A')", new { Id = id, TenantId, num.Numero, num.Sequencial, vm.Especie, vm.TipoSolicitacao, vm.Procedencia, vm.OrigemPedido, vm.Assunto, vm.Descricao, vm.InformacoesComplementares, vm.Interessado, vm.CpfCnpj, vm.Email, vm.Telefone, vm.SolicitanteNome, vm.SolicitanteMatricula, vm.SolicitanteCargo, Prioridade = prioridade, Status = status, vm.TipoProtocoloId, vm.AssuntoId, vm.PrioridadeId, vm.CanalEntradaId, vm.SetorOrigemId, SetorAtualId = vm.SetorOrigemId, vm.SetorDestinoId, UserId, UserName = UserNameSafe, DataAbertura = vm.SalvarComoRascunho ? (DateTime?)null : now, Now = now }, tx);
            await UpsertParticipanteAsync(db, tx, id, vm.SetorOrigemId, true, vm.SetorOrigemId == vm.SetorDestinoId);
            await UpsertParticipanteAsync(db, tx, id, vm.SetorDestinoId, true, !vm.SalvarComoRascunho);
            await RegistrarTramitacaoAsync(db, tx, id, vm.SetorOrigemId, vm.SetorDestinoId, "CRIACAO", null, status, "Protocolo criado.", null, null);
            await SalvarArquivosAsync(db, tx, id, vm.SetorOrigemId, await GetSetorNomeAsync(db, vm.SetorOrigemId) ?? "", arquivos, null, null);
            if (origemSolicitada)
            {
                var origemId = vm.GedDocumentId!.Value;
                var bloqueioNaGravacao = await GedOriginBlockReasonAsync(db, origemId, tx);
                if (bloqueioNaGravacao is not null)
                {
                    tx.Rollback();
                    ModelState.AddModelError(string.Empty, bloqueioNaGravacao);
                    vm.GedLinkNotice = ProtocolOriginLinkRules.Requirement;
                    return View(vm);
                }
                var vinculoId = Guid.NewGuid();
                var inseridos = await db.ExecuteAsync(@"
insert into ged.protocolo_documento_ged
(id, tenant_id, protocolo_id, protocolo_documento_id, ged_document_id, tipo_vinculo, observacao, criado_por, criado_por_nome, created_at, reg_status)
select @Id, @TenantId, @ProtocoloId, null, @GedDocumentId, 'DOCUMENTO_GERAL', null, @UserId, @UserName, now(), 'A'
where exists (select 1 from ged.document where tenant_id=@TenantId and id=@GedDocumentId and reg_status='A')
  and not exists (
      select 1 from ged.protocolo_documento_ged
      where tenant_id=@TenantId and protocolo_id=@ProtocoloId and ged_document_id=@GedDocumentId and reg_status='A'
  );", new { Id = vinculoId, TenantId, ProtocoloId = id, GedDocumentId = origemId, UserId, UserName = UserNameSafe }, tx);
                if (inseridos != 1)
                {
                    tx.Rollback();
                    ModelState.AddModelError(string.Empty, ProtocolOriginLinkRules.LinkNotSaved);
                    vm.GedLinkNotice = ProtocolOriginLinkRules.Requirement;
                    return View(vm);
                }
                await db.ExecuteAsync(@"
insert into ged.protocolo_auditoria
(tenant_id, protocolo_id, entidade, entidade_id, acao, valor_novo, usuario_id, usuario_nome, ip, user_agent)
values (@TenantId, @ProtocoloId, 'protocolo_documento_ged', @Id, 'GED_VINCULO', cast(@Json as jsonb), @UserId, @UserName, @Ip, @Ua);", new
                {
                    TenantId, ProtocoloId = id, Id = vinculoId,
                    Json = System.Text.Json.JsonSerializer.Serialize(new { gedDocumentId = origemId, tipoVinculo = "DOCUMENTO_GERAL" }),
                    UserId, UserName = UserNameSafe,
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Ua = Request.Headers.UserAgent.ToString()
                }, tx);
            }
            tx.Commit();
            committed = true;
        }
        catch (Exception ex)
        {
            if (!committed)
            {
                try { tx.Rollback(); } catch (Exception rbEx) { _logger?.LogWarning(rbEx, "Falha ao executar rollback da transação do protocolo {Id}.", id); }
            }
            _logger?.LogError(ex, "Falha na transação de criação do protocolo {Id}. OrigemSolicitada={OrigemSolicitada}", id, origemSolicitada);
            if (!origemSolicitada)
            {
                ModelState.AddModelError(string.Empty, "Não foi possível criar o protocolo devido a uma falha no banco de dados. Tente novamente.");
                return View(vm);
            }
            ModelState.AddModelError(string.Empty, ProtocolOriginLinkRules.LinkNotSaved);
            vm.GedLinkNotice = ProtocolOriginLinkRules.Requirement;
            return View(vm);
        }

        // Pós-commit: a criação e o vínculo estão confirmados no banco
        var vinculadoCriado = origemSolicitada ? " e vinculado ao documento de origem" : "";
        if (!vm.SalvarComoRascunho && vm.SetorOrigemId != vm.SetorDestinoId && UserId is not null)
        {
            try
            {
                var fwd = await _central.ForwardAsync(Actor(), id, null, vm.SetorDestinoId, "Abertura do protocolo", null, $"novo:{id:N}", null, null, null, HttpContext.RequestAborted);
                TempData[fwd.Success ? "ok" : "erro"] = fwd.Success
                    ? $"Protocolo {num.Numero} criado{vinculadoCriado}. {fwd.Message}"
                    : $"Protocolo {num.Numero} foi criado{vinculadoCriado} no setor de origem, mas o encaminhamento não foi concluído: {fwd.Message}";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Protocolo {Numero} ({Id}) criado, mas ocorreu falha durante o encaminhamento para o setor {SetorDestinoId}.", num.Numero, id, vm.SetorDestinoId);
                TempData["erro"] = $"Protocolo {num.Numero} foi criado{vinculadoCriado} no setor de origem, mas o encaminhamento está pendente devido a uma instabilidade temporária.";
            }
        }
        else
        {
            TempData["ok"] = origemSolicitada
                ? $"Protocolo {num.Numero} criado e vinculado ao documento de origem."
                : $"Protocolo {num.Numero} criado com sucesso.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<string?> GedOriginBlockReasonAsync(System.Data.IDbConnection db, Guid documentId, System.Data.IDbTransaction? tx)
    {
        if (UserId is not Guid uid) return "Sua sessão expirou. Entre novamente para criar o protocolo vinculado.";
        var active = await db.ExecuteScalarAsync<bool>("select exists(select 1 from ged.document where tenant_id=@TenantId and id=@Id and reg_status='A')", new { TenantId, Id = documentId }, tx);
        var canView = active && await _documentAuthorization.CanAccessDocumentAsync(TenantId, uid, documentId, "VIEW", new Dictionary<string, string>(), HttpContext.RequestAborted);
        var canEdit = canView && await _documentAuthorization.CanAccessDocumentAsync(TenantId, uid, documentId, "EDIT", new Dictionary<string, string>(), HttpContext.RequestAborted);
        return ProtocolOriginLinkRules.BlockReason(active, canView, canEdit);
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
        vm.Minutas = (await db.QueryAsync<ProtocoloMinutaVM>("""
select m.id Id, m.setor_id SetorId, coalesce(s.nome, 'Setor') SetorNome,
       m.titulo Titulo, m.conteudo Conteudo, m.status Status, m.versao Versao,
       m.updated_at UpdatedAt, m.atualizado_por_nome AtualizadoPorNome
from ged.protocolo_minuta m
left join ged.protocolo_setor s on s.tenant_id=m.tenant_id and s.id=m.setor_id
where m.tenant_id=@TenantId and m.protocolo_id=@Id and m.reg_status='A'
order by m.updated_at desc;
""", new { TenantId, Id = id })).ToList();
        vm.HistoricoMinutas = (await db.QueryAsync<ProtocoloMinutaHistoricoVM>("""
select minuta_id MinutaId, versao Versao, evento Evento, status Status,
       usuario_nome UsuarioNome, created_at CreatedAt
from ged.protocolo_minuta_historico
where tenant_id=@TenantId and protocolo_id=@Id
order by created_at desc
limit 100;
""", new { TenantId, Id = id })).ToList();
        var currentSectorOperator = (await GetSetorOperacaoAsync(db, vm.SetorAtualId)).HasValue && !fechado;
vm.PodeGerenciarMinutas = currentSectorOperator;
vm.PodeGerenciarPendencias = currentSectorOperator;
foreach (var minuta in vm.Minutas)
{
            var currentSector = currentSectorOperator && minuta.SetorId == vm.SetorAtualId;
            minuta.PodeEditar = currentSector && minuta.Status == "RASCUNHO";
            minuta.PodeConfirmar = currentSector && minuta.Status == "RASCUNHO";
            minuta.PodeEncaminhar = currentSector && minuta.Status == "CONFIRMADA";
            minuta.PodeDescartar = currentSector && minuta.Status is "RASCUNHO" or "CONFIRMADA";
        }
        vm.Pendencias = (await db.QueryAsync<ProtocoloPendenciaVM>("""
select p.id Id, p.setor_id SetorId, p.descricao Descricao, p.evidencia Evidencia,
       p.fonte_evidencia FonteEvidencia, p.origem Origem, p.status Status,
       p.atribuida_para AtribuidaPara, u.name ResponsavelNome, p.resolucao Resolucao,
       p.comprovante_documento_id ComprovanteDocumentoId, d.nome_arquivo ComprovanteNome,
       p.created_at CreatedAt
from ged.protocolo_pendencia p
left join ged.app_user u on u.tenant_id=p.tenant_id and u.id=p.atribuida_para
left join ged.protocolo_documento d on d.tenant_id=p.tenant_id and d.protocolo_id=p.protocolo_id
    and d.id=p.comprovante_documento_id
where p.tenant_id=@TenantId and p.protocolo_id=@Id and p.reg_status='A'
order by p.created_at desc;
""", new { TenantId, Id = id })).ToList();
        vm.HistoricoPendencias = (await db.QueryAsync<ProtocoloPendenciaHistoricoVM>("""
select pendencia_id PendenciaId, evento Evento, status Status,
       usuario_nome UsuarioNome, created_at CreatedAt
from ged.protocolo_pendencia_historico
where tenant_id=@TenantId and protocolo_id=@Id
order by created_at desc
limit 100;
""", new { TenantId, Id = id })).ToList();
        if (currentSectorOperator && vm.SetorAtualId.HasValue)
        {
            vm.ResponsaveisPendencia = (await db.QueryAsync<SelectListItem>("""
select u.id Value, coalesce(nullif(btrim(u.name), ''), u.email) Text
from ged.protocolo_usuario_setor us
join ged.app_user u on u.tenant_id=us.tenant_id and u.id=us.usuario_id
where us.tenant_id=@TenantId and us.setor_id=@SetorId and us.ativo=true and us.reg_status='A'
  and u.is_active=true and u.deleted_at_utc is null
order by Text;
""", new { TenantId, SetorId = vm.SetorAtualId })).ToList();
        }
        foreach (var pendencia in vm.Pendencias)
        {
            var currentSector = currentSectorOperator && pendencia.SetorId == vm.SetorAtualId;
            var active = pendencia.Status is "ABERTA" or "ATRIBUIDA";
            pendencia.PodeAtribuir = currentSector && active;
            pendencia.PodeResolver = currentSector && active &&
                (!pendencia.AtribuidaPara.HasValue || pendencia.AtribuidaPara == UserId || IsAdminOrGestor());
            pendencia.PodeDescartar = currentSector && active;
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
    public async Task<IActionResult> SalvarMinuta(Guid protocoloId, Guid? minutaId, string? titulo, string? conteudo, int? versao)
    {
        var normalizedTitle = (titulo ?? "").Trim();
        var normalizedContent = (conteudo ?? "").Trim();
        if (normalizedTitle.Length is 0 or > 200 || normalizedContent.Length is 0 or > 12000)
        {
            TempData["erro"] = "Informe título (até 200 caracteres) e conteúdo (até 12.000 caracteres) para a minuta.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        if (minutaId.HasValue && (!versao.HasValue || versao.Value < 1))
        {
            TempData["erro"] = "A versão da minuta está ausente. Recarregue o protocolo e tente novamente.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var db = await OpenAsync();
        var protocol = await GetBasicoAsync(db, protocoloId);
        if (protocol is null) return NotFound();
        var setorId = await GetSetorOperacaoAsync(db, protocol.SetorAtualId);
        if (!setorId.HasValue || StatusEncerrado(protocol.Status))
        {
            TempData["erro"] = "Sem permissão para alterar minutas neste protocolo.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();
        try
        {
            var lockedProtocol = await db.QuerySingleOrDefaultAsync<Basico>(
                "select id Id,status Status,setor_atual_id SetorAtualId from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A' for update",
                new { TenantId, Id = protocoloId }, tx);
            if (lockedProtocol is null || lockedProtocol.SetorAtualId != setorId || StatusEncerrado(lockedProtocol.Status))
            {
                tx.Rollback();
                TempData["erro"] = "A custódia do protocolo mudou. Recarregue a página antes de salvar a minuta.";
                return RedirectToAction(nameof(Details), new { id = protocoloId });
            }

            if (!minutaId.HasValue)
            {
                var id = Guid.NewGuid();
                await db.ExecuteAsync("""
insert into ged.protocolo_minuta (
    id, tenant_id, protocolo_id, setor_id, titulo, conteudo, status, versao,
    criado_por, criado_por_nome, atualizado_por, atualizado_por_nome, created_at, updated_at, reg_status
) values (
    @Id, @TenantId, @ProtocoloId, @SetorId, @Titulo, @Conteudo, 'RASCUNHO', 1,
    @UserId, @UserName, @UserId, @UserName, now(), now(), 'A'
);
""", new { Id = id, TenantId, ProtocoloId = protocoloId, SetorId = setorId, Titulo = normalizedTitle, Conteudo = normalizedContent, UserId, UserName = UserNameSafe }, tx);
                await WriteMinutaHistoryAsync(db, tx, id, protocoloId, 1, "CRIADA", "RASCUNHO", normalizedTitle, normalizedContent, new { source = "manual" });
            }
            else
            {
                var draft = await db.QuerySingleOrDefaultAsync<ProtocolDraftEditRow>("""
select id Id, setor_id SetorId, titulo Titulo, conteudo Conteudo, status Status, versao Versao
from ged.protocolo_minuta
where tenant_id=@TenantId and protocolo_id=@ProtocoloId and id=@MinutaId and reg_status='A'
for update;
""", new { TenantId, ProtocoloId = protocoloId, MinutaId = minutaId }, tx);
                if (draft is null || draft.SetorId != setorId || draft.Status != "RASCUNHO")
                {
                    tx.Rollback();
                    TempData["erro"] = "Esta minuta não pode mais ser editada no setor atual.";
                    return RedirectToAction(nameof(Details), new { id = protocoloId });
                }
                if (draft.Versao != versao)
                {
                    tx.Rollback();
                    TempData["erro"] = "A minuta foi alterada por outra pessoa. Recarregue a página antes de salvar.";
                    return RedirectToAction(nameof(Details), new { id = protocoloId });
                }

                var nextVersion = draft.Versao + 1;
                var updated = await db.ExecuteAsync("""
update ged.protocolo_minuta
set titulo=@Titulo, conteudo=@Conteudo, versao=@NextVersion,
    atualizado_por=@UserId, atualizado_por_nome=@UserName, updated_at=now()
where tenant_id=@TenantId and id=@MinutaId and status='RASCUNHO' and versao=@CurrentVersion;
""", new { Titulo = normalizedTitle, Conteudo = normalizedContent, NextVersion = nextVersion, UserId, UserName = UserNameSafe, TenantId, MinutaId = minutaId, CurrentVersion = versao }, tx);
                if (updated != 1)
                {
                    tx.Rollback();
                    TempData["erro"] = "A minuta foi alterada por outra pessoa. Recarregue a página antes de salvar.";
                    return RedirectToAction(nameof(Details), new { id = protocoloId });
                }
                await WriteMinutaHistoryAsync(db, tx, minutaId.Value, protocoloId, nextVersion, "EDITADA", "RASCUNHO", normalizedTitle, normalizedContent, new { previousVersion = draft.Versao });
            }

            tx.Commit();
            TempData["ok"] = minutaId.HasValue ? "Minuta atualizada." : "Minuta criada como rascunho.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarMinuta(Guid protocoloId, Guid minutaId, int versao)
    {
        using var db = await OpenAsync();
        var protocol = await GetBasicoAsync(db, protocoloId);
        if (protocol is null) return NotFound();
        var setorId = await GetSetorOperacaoAsync(db, protocol.SetorAtualId);
        if (!setorId.HasValue || StatusEncerrado(protocol.Status))
        {
            TempData["erro"] = "Sem permissão para confirmar minutas neste protocolo.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();
        try
        {
            var lockedProtocol = await db.QuerySingleOrDefaultAsync<Basico>(
                "select id Id,status Status,setor_atual_id SetorAtualId from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A' for update",
                new { TenantId, Id = protocoloId }, tx);
            var draft = await db.QuerySingleOrDefaultAsync<ProtocolDraftEditRow>("""
select id Id, setor_id SetorId, titulo Titulo, conteudo Conteudo, status Status, versao Versao
from ged.protocolo_minuta
where tenant_id=@TenantId and protocolo_id=@ProtocoloId and id=@MinutaId and reg_status='A'
for update;
""", new { TenantId, ProtocoloId = protocoloId, MinutaId = minutaId }, tx);
            if (lockedProtocol is null || lockedProtocol.SetorAtualId != setorId || StatusEncerrado(lockedProtocol.Status) ||
                draft is null || draft.SetorId != setorId || draft.Status != "RASCUNHO" || draft.Versao != versao)
            {
                tx.Rollback();
                TempData["erro"] = "A minuta não está mais elegível para confirmação. Recarregue o protocolo.";
                return RedirectToAction(nameof(Details), new { id = protocoloId });
            }

            var updated = await db.ExecuteAsync("""
update ged.protocolo_minuta
set status='CONFIRMADA', confirmada_por=@UserId, confirmada_em=now(),
    atualizado_por=@UserId, atualizado_por_nome=@UserName, updated_at=now()
where tenant_id=@TenantId and id=@MinutaId and status='RASCUNHO' and versao=@Versao;
""", new { UserId, UserName = UserNameSafe, TenantId, MinutaId = minutaId, Versao = versao }, tx);
            if (updated != 1)
                throw new InvalidOperationException("A minuta não foi confirmada porque seu estado mudou.");
            await WriteMinutaHistoryAsync(db, tx, minutaId, protocoloId, draft.Versao, "CONFIRMADA", "CONFIRMADA", draft.Titulo, draft.Conteudo, new { version = versao });
            tx.Commit();
            TempData["ok"] = "Minuta confirmada. Ela pode ser encaminhada pelo fluxo normal de tramitação.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DescartarMinuta(Guid protocoloId, Guid minutaId, int versao)
    {
        using var db = await OpenAsync();
        var protocol = await GetBasicoAsync(db, protocoloId);
        if (protocol is null) return NotFound();
        var setorId = await GetSetorOperacaoAsync(db, protocol.SetorAtualId);
        if (!setorId.HasValue || StatusEncerrado(protocol.Status))
        {
            TempData["erro"] = "Sem permissão para descartar minutas neste protocolo.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();
        try
        {
            var lockedProtocol = await db.QuerySingleOrDefaultAsync<Basico>(
                "select id Id,status Status,setor_atual_id SetorAtualId from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A' for update",
                new { TenantId, Id = protocoloId }, tx);
            var draft = await db.QuerySingleOrDefaultAsync<ProtocolDraftEditRow>("""
select id Id, setor_id SetorId, titulo Titulo, conteudo Conteudo, status Status, versao Versao
from ged.protocolo_minuta
where tenant_id=@TenantId and protocolo_id=@ProtocoloId and id=@MinutaId and reg_status='A'
for update;
""", new { TenantId, ProtocoloId = protocoloId, MinutaId = minutaId }, tx);
            if (lockedProtocol is null || lockedProtocol.SetorAtualId != setorId || StatusEncerrado(lockedProtocol.Status) ||
                draft is null || draft.SetorId != setorId || draft.Status is not ("RASCUNHO" or "CONFIRMADA") || draft.Versao != versao)
            {
                tx.Rollback();
                TempData["erro"] = "A minuta não está mais elegível para descarte. Recarregue o protocolo.";
                return RedirectToAction(nameof(Details), new { id = protocoloId });
            }

            var updated = await db.ExecuteAsync("""
update ged.protocolo_minuta
set status='DESCARTADA', descartada_por=@UserId, descartada_em=now(),
    atualizado_por=@UserId, atualizado_por_nome=@UserName, updated_at=now()
where tenant_id=@TenantId and id=@MinutaId and status in ('RASCUNHO','CONFIRMADA') and versao=@Versao;
""", new { UserId, UserName = UserNameSafe, TenantId, MinutaId = minutaId, Versao = versao }, tx);
            if (updated != 1)
                throw new InvalidOperationException("A minuta não foi descartada porque seu estado mudou.");
            await WriteMinutaHistoryAsync(db, tx, minutaId, protocoloId, draft.Versao, "DESCARTADA", "DESCARTADA", draft.Titulo, draft.Conteudo, new { previousStatus = draft.Status });
            tx.Commit();
            TempData["ok"] = "Minuta descartada; o histórico foi preservado.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EncaminharMinuta(Guid protocoloId, Guid minutaId, Guid setorDestinoId)
    {
        var result = await _central.ForwardAsync(
            Actor(), protocoloId, null, setorDestinoId, null, null,
            $"protocolo-minuta:{minutaId:N}", null, null, null,
            HttpContext.RequestAborted, minutaId);
        TempData[result.Success ? "ok" : "erro"] = result.Message;
        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarPendencia(Guid protocoloId, string? descricao)
    {
        var item = (descricao ?? "").Trim();
        if (item.Length is 0 or > 500)
            return PendingActionError(protocoloId, "Informe a descrição da pendência (até 500 caracteres).");

        using var db = await OpenAsync();
        var protocol = await GetBasicoAsync(db, protocoloId);
        if (protocol is null) return NotFound();
        var setorId = await GetSetorOperacaoAsync(db, protocol.SetorAtualId);
        if (!setorId.HasValue || StatusEncerrado(protocol.Status))
            return PendingActionError(protocoloId, "Sem permissão para registrar pendências neste protocolo.");

        using var tx = db.BeginTransaction();
        try
        {
            var lockedProtocol = await LockCurrentProtocolForPendingAsync(db, tx, protocoloId, setorId.Value);
            if (lockedProtocol is null)
            {
                tx.Rollback();
                return PendingActionError(protocoloId, "A custódia do protocolo mudou. Recarregue a página e tente novamente.");
            }
            var pendingId = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.protocolo_pendencia (
    id, tenant_id, protocolo_id, setor_id, descricao, origem, status,
    confirmada_por, confirmada_em, created_at, updated_at, reg_status
) values (
    @Id, @TenantId, @ProtocolId, @SectorId, @Description, 'HUMANA', 'ABERTA',
    @UserId, now(), now(), now(), 'A'
);
""", new { Id = pendingId, TenantId, ProtocolId = protocoloId, SectorId = setorId, Description = item, UserId }, tx);
            await WritePendenciaHistoryAsync(db, tx, pendingId, protocoloId, "CRIADA", "ABERTA", item, null, null, null, new { origin = "human" });
            tx.Commit();
            TempData["ok"] = "Pendência documental registrada.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AtribuirPendencia(Guid protocoloId, Guid pendenciaId, Guid responsavelId)
    {
        using var db = await OpenAsync();
        var protocol = await GetBasicoAsync(db, protocoloId);
        if (protocol is null) return NotFound();
        var setorId = await GetSetorOperacaoAsync(db, protocol.SetorAtualId);
        if (!setorId.HasValue || StatusEncerrado(protocol.Status) || responsavelId == Guid.Empty)
            return PendingActionError(protocoloId, "Sem permissão ou responsável inválido para atribuir a pendência.");

        using var tx = db.BeginTransaction();
        try
        {
            var lockedProtocol = await LockCurrentProtocolForPendingAsync(db, tx, protocoloId, setorId.Value);
            var pending = await LockPendingForUpdateAsync(db, tx, protocoloId, pendenciaId);
            if (lockedProtocol is null || pending is null || pending.SetorId != setorId ||
                pending.Status is not ("ABERTA" or "ATRIBUIDA"))
            {
                tx.Rollback();
                return PendingActionError(protocoloId, "A pendência não está mais disponível para atribuição neste setor.");
            }
            var activeMember = await db.ExecuteScalarAsync<bool>("""
select exists (
    select 1 from ged.protocolo_usuario_setor us
    join ged.app_user u on u.tenant_id=us.tenant_id and u.id=us.usuario_id
    where us.tenant_id=@TenantId and us.usuario_id=@Assignee and us.setor_id=@SectorId
      and us.ativo=true and us.reg_status='A' and u.is_active=true and u.deleted_at_utc is null
);
""", new { TenantId, Assignee = responsavelId, SectorId = setorId }, tx);
            if (!activeMember)
            {
                tx.Rollback();
                return PendingActionError(protocoloId, "O responsável precisa ser um usuário ativo do setor atual.");
            }
            var updated = await db.ExecuteAsync("""
update ged.protocolo_pendencia
set status='ATRIBUIDA', atribuida_para=@Assignee, atribuida_por=@UserId,
    atribuida_em=now(), updated_at=now()
where tenant_id=@TenantId and id=@PendingId and protocolo_id=@ProtocolId
  and setor_id=@SectorId and status in ('ABERTA','ATRIBUIDA');
""", new { Assignee = responsavelId, UserId, TenantId, PendingId = pendenciaId, ProtocolId = protocoloId, SectorId = setorId }, tx);
            if (updated != 1)
                throw new InvalidOperationException("A pendência mudou durante a atribuição.");
            await WritePendenciaHistoryAsync(db, tx, pendenciaId, protocoloId, "ATRIBUIDA", "ATRIBUIDA", pending.Descricao, responsavelId, null, null, new { assigneeId = responsavelId });
            tx.Commit();
            TempData["ok"] = "Pendência atribuída ao responsável selecionado.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolverPendencia(Guid protocoloId, Guid pendenciaId, string? resolucao, Guid comprovanteDocumentoId)
    {
        var resolution = (resolucao ?? "").Trim();
        if (resolution.Length is 0 or > 2000 || comprovanteDocumentoId == Guid.Empty)
            return PendingActionError(protocoloId, "Informe a justificativa e selecione um comprovante vinculado ao protocolo.");

        using var db = await OpenAsync();
        var protocol = await GetBasicoAsync(db, protocoloId);
        if (protocol is null) return NotFound();
        var setorId = await GetSetorOperacaoAsync(db, protocol.SetorAtualId);
        if (!setorId.HasValue || StatusEncerrado(protocol.Status))
            return PendingActionError(protocoloId, "Sem permissão para resolver pendências neste protocolo.");

        using var tx = db.BeginTransaction();
        try
        {
            var lockedProtocol = await LockCurrentProtocolForPendingAsync(db, tx, protocoloId, setorId.Value);
            var pending = await LockPendingForUpdateAsync(db, tx, protocoloId, pendenciaId);
            if (lockedProtocol is null || pending is null || pending.SetorId != setorId ||
                pending.Status is not ("ABERTA" or "ATRIBUIDA") ||
                (pending.AtribuidaPara.HasValue && pending.AtribuidaPara != UserId && !IsAdminOrGestor()))
            {
                tx.Rollback();
                return PendingActionError(protocoloId, "A pendência não está mais elegível para resolução por este usuário.");
            }
            var proofExists = await db.ExecuteScalarAsync<bool>("""
select exists (
    select 1 from ged.protocolo_documento
    where tenant_id=@TenantId and protocolo_id=@ProtocolId and id=@ProofId and reg_status='A'
);
""", new { TenantId, ProtocolId = protocoloId, ProofId = comprovanteDocumentoId }, tx);
            if (!proofExists)
            {
                tx.Rollback();
                return PendingActionError(protocoloId, "O comprovante precisa ser um documento ativo já vinculado a este protocolo.");
            }
            var updated = await db.ExecuteAsync("""
update ged.protocolo_pendencia
set status='RESOLVIDA', resolucao=@Resolution, comprovante_documento_id=@ProofId,
    resolvida_por=@UserId, resolvida_em=now(), updated_at=now()
where tenant_id=@TenantId and id=@PendingId and protocolo_id=@ProtocolId
  and setor_id=@SectorId and status in ('ABERTA','ATRIBUIDA');
""", new { Resolution = resolution, ProofId = comprovanteDocumentoId, UserId, TenantId, PendingId = pendenciaId, ProtocolId = protocoloId, SectorId = setorId }, tx);
            if (updated != 1)
                throw new InvalidOperationException("A pendência mudou durante a resolução.");
            await WritePendenciaHistoryAsync(db, tx, pendenciaId, protocoloId, "RESOLVIDA", "RESOLVIDA", pending.Descricao, pending.AtribuidaPara, resolution, comprovanteDocumentoId, new { proofDocumentId = comprovanteDocumentoId });
            tx.Commit();
            TempData["ok"] = "Pendência resolvida com justificativa e comprovante vinculados.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DescartarPendencia(Guid protocoloId, Guid pendenciaId, string? motivo)
    {
        var reason = (motivo ?? "").Trim();
        if (reason.Length is 0 or > 1000)
            return PendingActionError(protocoloId, "Informe o motivo do descarte (até 1.000 caracteres).");

        using var db = await OpenAsync();
        var protocol = await GetBasicoAsync(db, protocoloId);
        if (protocol is null) return NotFound();
        var setorId = await GetSetorOperacaoAsync(db, protocol.SetorAtualId);
        if (!setorId.HasValue || StatusEncerrado(protocol.Status))
            return PendingActionError(protocoloId, "Sem permissão para descartar pendências neste protocolo.");

        using var tx = db.BeginTransaction();
        try
        {
            var lockedProtocol = await LockCurrentProtocolForPendingAsync(db, tx, protocoloId, setorId.Value);
            var pending = await LockPendingForUpdateAsync(db, tx, protocoloId, pendenciaId);
            if (lockedProtocol is null || pending is null || pending.SetorId != setorId ||
                pending.Status is not ("ABERTA" or "ATRIBUIDA"))
            {
                tx.Rollback();
                return PendingActionError(protocoloId, "A pendência não está mais elegível para descarte.");
            }
            var updated = await db.ExecuteAsync("""
update ged.protocolo_pendencia
set status='DESCARTADA', motivo_descarte=@Reason, descartada_por=@UserId,
    descartada_em=now(), updated_at=now()
where tenant_id=@TenantId and id=@PendingId and protocolo_id=@ProtocolId
  and setor_id=@SectorId and status in ('ABERTA','ATRIBUIDA');
""", new { Reason = reason, UserId, TenantId, PendingId = pendenciaId, ProtocolId = protocoloId, SectorId = setorId }, tx);
            if (updated != 1)
                throw new InvalidOperationException("A pendência mudou durante o descarte.");
            await WritePendenciaHistoryAsync(db, tx, pendenciaId, protocoloId, "DESCARTADA", "DESCARTADA", pending.Descricao, pending.AtribuidaPara, reason, null, new { reason });
            tx.Commit();
            TempData["ok"] = "Pendência descartada; o motivo e o histórico foram preservados.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        catch
        {
            tx.Rollback();
            throw;
        }
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

        // Integrar eventos de protocolo com temporalidade para documentos GED vinculados cuja regra use este evento
        var isEncerramento = status is "FINALIZADO" or "DEFERIDO" or "INDEFERIDO";
        var isArquivamento = status == "ARQUIVADO";

        if (isEncerramento || isArquivamento)
        {
            if (UserId is not Guid actorId)
            {
                tx.Rollback();
                return Challenge();
            }

            const string findDocsSql = """
SELECT 
    d.id as DocumentId,
    d.closed_at as ClosedAt,
    d.archived_at as ArchivedAt,
    COALESCE(pvi.retention_start_event::text, cp.retention_start_event::text) as StartEvent
FROM ged.protocolo_documento_ged pdg
JOIN ged.document d ON d.tenant_id = pdg.tenant_id AND d.id = pdg.ged_document_id AND d.reg_status = 'A'
LEFT JOIN ged.classification_plan cp ON cp.tenant_id = d.tenant_id AND cp.id = d.classification_id
LEFT JOIN ged.classification_plan_version_item pvi 
    ON pvi.tenant_id = d.tenant_id 
   AND pvi.version_id = d.classification_version_id 
   AND pvi.classification_id = d.classification_id
WHERE pdg.tenant_id = @TenantId 
  AND pdg.protocolo_id = @Id 
  AND pdg.reg_status = 'A'
FOR UPDATE OF d;
""";
            var linkedDocs = (await db.QueryAsync<(Guid DocumentId, DateTime? ClosedAt, DateTime? ArchivedAt, string? StartEvent)>(
                findDocsSql, new { TenantId, Id = id }, tx)).ToList();
            var affectedDocs = linkedDocs
                .Where(x => (isEncerramento && x.StartEvent == "ENCERRAMENTO" && !x.ClosedAt.HasValue)
                         || (isArquivamento && x.StartEvent == "ARQUIVAMENTO" && !x.ArchivedAt.HasValue))
                .Select(x => x.DocumentId)
                .Distinct()
                .ToArray();
            var allowedDocs = affectedDocs.Length == 0
                ? new HashSet<Guid>()
                : await _documentAuthorization.FilterDocumentsAsync(TenantId, actorId, affectedDocs, "EDIT", HttpContext.RequestAborted);
            if (affectedDocs.Any(x => !allowedDocs.Contains(x)))
            {
                tx.Rollback();
                return Forbid();
            }

            foreach (var docItem in linkedDocs)
            {
                var otherActiveProtocolsCount = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
SELECT COUNT(1)
FROM ged.protocolo_documento_ged pdg
JOIN ged.protocolo p ON p.tenant_id = pdg.tenant_id AND p.id = pdg.protocolo_id AND p.reg_status = 'A'
WHERE pdg.tenant_id = @TenantId
  AND pdg.ged_document_id = @DocId
  AND pdg.protocolo_id <> @ProtocoloId
  AND pdg.reg_status = 'A'
  AND UPPER(p.status) NOT IN ('FINALIZADO', 'ENCERRADO', 'ARQUIVADO', 'CANCELADO');
""", new { TenantId, DocId = docItem.DocumentId, ProtocoloId = id }, tx, cancellationToken: HttpContext.RequestAborted));

                if (otherActiveProtocolsCount > 0)
                {
                    await db.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.protocolo_tramitacao(
    tenant_id, protocolo_id, setor_origem_id, setor_destino_id, usuario_id, usuario_nome,
    acao, status_anterior, status_novo, justificativa, observacao, ativa)
VALUES (@TenantId, @Id, @Setor, @Setor, @UserId, @UserName, 'EFEITO_ARQUIVISTICO', @Novo, @Novo,
    'Efeito arquivístico mantido pendente: documento GED possui outros protocolos ativos vinculados.',
    @DocInfo, false);
""", new { TenantId, Id = id, Setor = p.SetorAtualId, UserId, UserName = UserNameSafe, Novo = status, DocInfo = $"Documento {docItem.DocumentId} vinculado a {otherActiveProtocolsCount} outro(s) protocolo(s) ativo(s)." }, tx, cancellationToken: HttpContext.RequestAborted));
                    continue;
                }

                if (isEncerramento && docItem.StartEvent == "ENCERRAMENTO" && !docItem.ClosedAt.HasValue)
                {
                    await db.ExecuteAsync("""
UPDATE ged.document 
SET closed_at = NOW(), updated_at = NOW(), updated_by = @UserId
WHERE tenant_id = @TenantId AND id = @DocId;
""", new { TenantId, DocId = docItem.DocumentId, UserId }, tx);
                    await _retentionJobs.EnqueueRecalculateAsync(db, tx, TenantId, docItem.DocumentId, "PROTOCOL_CLOSED_EVENT", HttpContext.RequestAborted);
                }
                else if (isArquivamento && docItem.StartEvent == "ARQUIVAMENTO" && !docItem.ArchivedAt.HasValue)
                {
                    await db.ExecuteAsync("""
UPDATE ged.document 
SET archived_at = NOW(), updated_at = NOW(), updated_by = @UserId
WHERE tenant_id = @TenantId AND id = @DocId;
""", new { TenantId, DocId = docItem.DocumentId, UserId }, tx);
                    await _retentionJobs.EnqueueRecalculateAsync(db, tx, TenantId, docItem.DocumentId, "PROTOCOL_ARCHIVED_EVENT", HttpContext.RequestAborted);
                }
            }
        }

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
    private async Task<Basico?> LockCurrentProtocolForPendingAsync(IDbConnection db, IDbTransaction tx, Guid protocoloId, Guid expectedSectorId)
    {
        var protocol = await db.QuerySingleOrDefaultAsync<Basico>(
            "select id Id,status Status,setor_atual_id SetorAtualId from ged.protocolo where tenant_id=@TenantId and id=@Id and reg_status='A' for update",
            new { TenantId, Id = protocoloId }, tx);
        return protocol is not null && protocol.SetorAtualId == expectedSectorId && !StatusEncerrado(protocol.Status)
            ? protocol
            : null;
    }
    private Task<ProtocolPendingEditRow?> LockPendingForUpdateAsync(IDbConnection db, IDbTransaction tx, Guid protocoloId, Guid pendenciaId) =>
        db.QuerySingleOrDefaultAsync<ProtocolPendingEditRow>("""
select id Id, setor_id SetorId, descricao Descricao, status Status, atribuida_para AtribuidaPara
from ged.protocolo_pendencia
where tenant_id=@TenantId and protocolo_id=@ProtocolId and id=@PendingId and reg_status='A'
for update;
""", new { TenantId, ProtocolId = protocoloId, PendingId = pendenciaId }, tx);
    private async Task WritePendenciaHistoryAsync(IDbConnection db, IDbTransaction tx, Guid pendenciaId, Guid protocoloId, string evento, string status, string descricao, Guid? atribuidaPara, string? resolucao, Guid? comprovanteDocumentoId, object details)
    {
        var rows = await db.ExecuteAsync("""
insert into ged.protocolo_pendencia_historico (
    id, tenant_id, protocolo_id, pendencia_id, evento, status, descricao,
    atribuida_para, resolucao, comprovante_documento_id, usuario_id, usuario_nome,
    detalhes, created_at
) values (
    @Id, @TenantId, @ProtocolId, @PendingId, @Evento, @Status, @Descricao,
    @AssignedTo, @Resolution, @ProofId, @UserId, @UserName, @Details::jsonb, now()
);
""", new
        {
            Id = Guid.NewGuid(),
            TenantId,
            ProtocolId = protocoloId,
            PendingId = pendenciaId,
            Evento = evento,
            Status = status,
            Descricao = descricao,
            AssignedTo = atribuidaPara,
            Resolution = resolucao,
            ProofId = comprovanteDocumentoId,
            UserId,
            UserName = UserNameSafe,
            Details = JsonSerializer.Serialize(details)
        }, tx);
        if (rows != 1)
            throw new InvalidOperationException("Falha ao registrar o histórico da pendência documental.");
    }
    private IActionResult PendingActionError(Guid protocoloId, string message)
    {
        TempData["erro"] = message;
        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }
    private async Task WriteMinutaHistoryAsync(IDbConnection db, IDbTransaction tx, Guid minutaId, Guid protocoloId, int versao, string evento, string status, string titulo, string conteudo, object details)
    {
        var rows = await db.ExecuteAsync("""
insert into ged.protocolo_minuta_historico (
    id, tenant_id, protocolo_id, minuta_id, versao, evento, status, titulo,
    conteudo, usuario_id, usuario_nome, detalhes, created_at
) values (
    @Id, @TenantId, @ProtocoloId, @MinutaId, @Versao, @Evento, @Status, @Titulo,
    @Conteudo, @UserId, @UserName, @Details::jsonb, now()
);
""", new
        {
            Id = Guid.NewGuid(),
            TenantId,
            ProtocoloId = protocoloId,
            MinutaId = minutaId,
            Versao = versao,
            Evento = evento,
            Status = status,
            Titulo = titulo,
            Conteudo = conteudo,
            UserId,
            UserName = UserNameSafe,
            Details = JsonSerializer.Serialize(details)
        }, tx);
        if (rows != 1)
            throw new InvalidOperationException("Falha ao registrar o histórico da minuta.");
    }
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
    private sealed class ProtocolDraftEditRow { public Guid Id { get; set; } public Guid SetorId { get; set; } public string Titulo { get; set; } = ""; public string Conteudo { get; set; } = ""; public string Status { get; set; } = ""; public int Versao { get; set; } }
    private sealed class ProtocolPendingEditRow { public Guid Id { get; set; } public Guid SetorId { get; set; } public string Descricao { get; set; } = ""; public string Status { get; set; } = ""; public Guid? AtribuidaPara { get; set; } }
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
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (Exception) { return StatusCode(500, new { success = false, message = "Erro ao processar assistência de IA." }); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiConfirmPending(Guid protocoloId, Guid executionId, long concurrencyToken, int pendingIndex, CancellationToken ct)
    {
        try
        {
            var result = await _aiAssist.ConfirmPendingItemAsync(
                new InovaGed.Application.Protocolo.ProtocolPendingConfirmRequest
                {
                    ProtocoloId = protocoloId,
                    ExecutionId = executionId,
                    ConcurrencyToken = concurrencyToken,
                    PendingIndex = pendingIndex
                }, ct);
            return Json(result);
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new { success = false, message = "Acesso não autorizado." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { success = false, message = "Erro ao confirmar a pendência documental." });
        }
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
        catch (Exception) { return StatusCode(500, new { success = false, message = "Erro ao aplicar assunto." }); }
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
        catch (Exception) { return StatusCode(500, new { success = false, message = "Erro ao aplicar minuta." }); }
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

    private sealed record ProtocoloResumoExistente(Guid Id, string Numero, string Status, Guid SetorAtualId, Guid SetorOrigemId, Guid? SetorDestinoInicialId);
}
