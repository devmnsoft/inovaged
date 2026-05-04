using System.Data;
using System.Security.Cryptography;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ProtocoloController : GedControllerBase
{
    private readonly ILogger<ProtocoloController> _logger;

    public ProtocoloController(IDbConnectionFactory dbFactory, ILogger<ProtocoloController> logger) : base(dbFactory)
    {
        _logger = logger;
    }

    // ==========================================================
    // GRID
    // Regra: o usuário só vê no grid protocolos cujo setor_atual_id
    // esteja vinculado a ele em ged.protocolo_usuario_setor.
    // Admin/Gestor/Auditor visualizam todos.
    // ==========================================================
    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? status)
    {
        using var db = await OpenAsync();

        var admin = IsAdminLike();
        var setorIds = await GetUserSetorIdsAsync(db);

        if (!admin && setorIds.Length == 0)
        {
            TempData["erro"] = "Seu usuário não está vinculado a nenhum setor do Protocolo. Cadastre o vínculo em ged.protocolo_usuario_setor.";
            return View(new ProtocoloIndexVM
            {
                Q = q,
                Status = status,
                Protocolos = Array.Empty<ProtocoloGridItemVM>()
            });
        }

        var sql = @"
select
    p.id,
    p.numero,
    coalesce(p.data_abertura, p.created_at) as Emissao,
    coalesce(sa.nome, '') as Lotacao,
    coalesce(t.nome, p.especie, '') as TipoSolicitacao,
    coalesce(p.interessado, '') as Interessado,
    coalesce(a.nome, p.assunto, '') as Assunto,
    p.status
from ged.protocolo p
left join ged.protocolo_setor sa on sa.id = p.setor_atual_id
left join ged.protocolo_tipo t on t.id = p.tipo_id
left join ged.protocolo_assunto a on a.id = p.assunto_id
where p.tenant_id = @tenantId
  and p.reg_status = 'A'
  and (
        @admin = true
        or p.setor_atual_id = any(@setorIds)
      )
  and (
        @q is null
        or p.numero ilike '%' || @q || '%'
        or p.assunto ilike '%' || @q || '%'
        or p.interessado ilike '%' || @q || '%'
      )
  and (
        @status is null
        or p.status = @status
      )
order by coalesce(p.data_abertura, p.created_at) desc, p.numero desc;";

        var rows = await db.QueryAsync<ProtocoloGridItemVM>(sql, new
        {
            tenantId = TenantId,
            admin,
            setorIds,
            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            status = string.IsNullOrWhiteSpace(status) ? null : status.Trim()
        });

        return View(new ProtocoloIndexVM
        {
            Q = q,
            Status = status,
            Protocolos = rows.ToList()
        });
    }

    // ==========================================================
    // NOVO
    // Permite vários anexos desde a criação.
    // ==========================================================
    [HttpGet]
    public async Task<IActionResult> Novo()
    {
        using var db = await OpenAsync();
        var vm = new ProtocoloFormVM();
        await PopulateFormListsAsync(db, vm);

        var userSetores = await GetUserSetorIdsAsync(db);
        if (userSetores.Length > 0)
        {
            vm.SetorOrigemId = userSetores[0];
            vm.SetorDestinoId = userSetores[0];
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1024L * 1024L * 200L)]
    public async Task<IActionResult> Novo(ProtocoloFormVM vm)
    {
        using var db = await OpenAsync();
        await PopulateFormListsAsync(db, vm);

        if (string.IsNullOrWhiteSpace(vm.Assunto))
            ModelState.AddModelError(nameof(vm.Assunto), "Informe o assunto do protocolo.");

        if (vm.SetorOrigemId == Guid.Empty)
            ModelState.AddModelError(nameof(vm.SetorOrigemId), "Informe o setor de origem.");

        if (vm.SetorDestinoId == Guid.Empty)
            ModelState.AddModelError(nameof(vm.SetorDestinoId), "Informe o setor para tramitação inicial.");

        if (!ModelState.IsValid)
            return View(vm);

        var userSetores = await GetUserSetorIdsAsync(db);
        if (!IsAdminLike() && !userSetores.Contains(vm.SetorOrigemId))
        {
            TempData["erro"] = "Você não pode abrir protocolo em nome de um setor ao qual não está vinculado.";
            return View(vm);
        }

        using var tx = db.BeginTransaction();

        try
        {
            var now = DateTime.Now;
            var numero = await db.ExecuteScalarAsync<string>(
                "select ged.next_protocolo_numero(@tenantId);",
                new { tenantId = TenantId },
                tx);

            var status = vm.SalvarComoRascunho
                ? "RASCUNHO"
                : (vm.SetorDestinoId == vm.SetorOrigemId ? "ABERTO" : "EM_TRAMITACAO");

            var dataAbertura = vm.SalvarComoRascunho ? (DateTime?)null : now;

            var protocoloId = await db.ExecuteScalarAsync<Guid>(@"
insert into ged.protocolo
(
    tenant_id, numero, ano, tipo_id, assunto_id, prioridade_id, canal_entrada_id,
    assunto, especie, procedencia, interessado, cpf_cnpj, email, telefone,
    descricao, informacoes_complementares, status,
    setor_origem_id, setor_atual_id, criado_por, criado_por_nome,
    data_abertura, created_at, reg_status
)
values
(
    @tenantId, @numero, extract(year from now()), @tipoId, @assuntoId, @prioridadeId, @canalEntradaId,
    @assunto, @especie, @procedencia, @interessado, @cpfCnpj, @email, @telefone,
    @descricao, @informacoesComplementares, @status,
    @setorOrigemId, @setorDestinoId, @userId, @userName,
    @dataAbertura, @now, 'A'
)
returning id;",
                new
                {
                    tenantId = TenantId,
                    numero,
                    tipoId = vm.TipoId,
                    assuntoId = vm.AssuntoId,
                    prioridadeId = vm.PrioridadeId,
                    canalEntradaId = vm.CanalEntradaId,
                    assunto = vm.Assunto.Trim(),
                    especie = vm.Especie,
                    procedencia = vm.Procedencia,
                    interessado = vm.Interessado,
                    cpfCnpj = vm.CpfCnpj,
                    email = vm.Email,
                    telefone = vm.Telefone,
                    descricao = vm.Descricao,
                    informacoesComplementares = vm.InformacoesComplementares,
                    status,
                    setorOrigemId = vm.SetorOrigemId,
                    setorDestinoId = vm.SetorDestinoId,
                    userId = UserId,
                    userName = UserNameSafe,
                    dataAbertura,
                    now
                },
                tx);

            await UpsertParticipanteAsync(db, tx, protocoloId, vm.SetorOrigemId, true, vm.SetorOrigemId == vm.SetorDestinoId);
            await UpsertParticipanteAsync(db, tx, protocoloId, vm.SetorDestinoId, true, true);

            await InsertTramitacaoAsync(db, tx,
                protocoloId,
                vm.SetorOrigemId,
                vm.SetorDestinoId,
                "CRIACAO",
                null,
                status,
                "Protocolo criado.",
                vm.ObservacaoInicial,
                null);

            if (!string.IsNullOrWhiteSpace(vm.ObservacaoInicial))
            {
                await InsertObservacaoAsync(db, tx, protocoloId, vm.SetorOrigemId, "PUBLICA", vm.ObservacaoInicial);
            }

            await SaveUploadedFilesAsync(db, tx, protocoloId, vm.SetorOrigemId, Request.Form.Files, null, "Anexo incluído na criação do protocolo.");

            await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo", protocoloId, "CRIACAO", null,
                new { numero, status, vm.SetorOrigemId, vm.SetorDestinoId });

            tx.Commit();

            TempData["ok"] = $"Protocolo {numero} criado com sucesso.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Erro ao criar protocolo.");
            TempData["erro"] = "Erro ao criar protocolo.";
            return View(vm);
        }
    }

    // ==========================================================
    // DETALHES
    // Permite visualizar se o usuário é setor atual, participante
    // histórico ou Admin/Gestor/Auditor.
    // O GRID continua mostrando apenas setor atual.
    // ==========================================================
    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        using var db = await OpenAsync();

        if (!await PodeVisualizarAsync(db, id))
        {
            TempData["erro"] = "Você não tem permissão para visualizar este protocolo.";
            return RedirectToAction(nameof(Index));
        }

        var header = await db.QueryFirstOrDefaultAsync<ProtocoloDetailsVM>(@"
select
    p.id,
    p.numero,
    coalesce(p.data_abertura, p.created_at) as Emissao,
    p.status,
    coalesce(t.nome, p.especie, '') as TipoSolicitacao,
    coalesce(pr.nome, '') as Prioridade,
    coalesce(ce.nome, '') as CanalEntrada,
    coalesce(a.nome, p.assunto, '') as Assunto,
    p.especie,
    p.procedencia,
    p.interessado,
    p.cpf_cnpj as CpfCnpj,
    p.email,
    p.telefone,
    p.descricao,
    p.informacoes_complementares as InformacoesComplementares,
    p.justificativa_encerramento as JustificativaEncerramento,
    so.nome as SetorOrigem,
    sa.nome as SetorAtual,
    p.setor_atual_id as SetorAtualId
from ged.protocolo p
left join ged.protocolo_tipo t on t.id = p.tipo_id
left join ged.protocolo_assunto a on a.id = p.assunto_id
left join ged.protocolo_prioridade pr on pr.id = p.prioridade_id
left join ged.protocolo_canal_entrada ce on ce.id = p.canal_entrada_id
left join ged.protocolo_setor so on so.id = p.setor_origem_id
left join ged.protocolo_setor sa on sa.id = p.setor_atual_id
where p.tenant_id = @tenantId and p.id = @id and p.reg_status = 'A';",
            new { tenantId = TenantId, id });

        if (header is null)
        {
            TempData["erro"] = "Protocolo não encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var podeAtuar = await PodeAtuarNoSetorAtualAsync(db, id);

        header.PodeAtuar = podeAtuar;
        header.PodeAnexar = podeAtuar && IsStatusOperacional(header.Status);
        header.PodeTramitar = podeAtuar && IsStatusOperacional(header.Status);
        header.PodeFinalizar = podeAtuar && IsStatusOperacional(header.Status);
        header.PodeArquivar = podeAtuar && header.Status != "ARQUIVADO" && header.Status != "CANCELADO";

        var userSetores = await GetUserSetorIdsAsync(db);

        var documentos = (await db.QueryAsync<ProtocoloDocumentoVM>(@"
select
    d.id,
    d.nome_arquivo as NomeArquivo,
    d.content_type as ContentType,
    d.tamanho_bytes as TamanhoBytes,
    coalesce(td.nome, d.tipo_documento, '') as TipoDocumento,
    d.descricao,
    coalesce(s.nome, '') as Setor,
    d.setor_id as SetorId,
    coalesce(d.anexado_por_nome, '') as AnexadoPor,
    d.created_at as CreatedAt
from ged.protocolo_documento d
left join ged.protocolo_setor s on s.id = d.setor_id
left join ged.protocolo_tipo_documento td on td.id = d.tipo_documento_id
where d.tenant_id = @tenantId
  and d.protocolo_id = @id
  and d.reg_status = 'A'
order by d.created_at desc;",
            new { tenantId = TenantId, id })).ToList();

        foreach (var d in documentos)
        {
            d.PodeExcluir = IsAdminLike() || (d.SetorId.HasValue && userSetores.Contains(d.SetorId.Value) && IsStatusOperacional(header.Status));
        }

        header.Documentos = documentos;

        header.Tramitacoes = (await db.QueryAsync<ProtocoloTramitacaoVM>(@"
select
    t.id,
    t.acao,
    t.status_anterior as StatusAnterior,
    t.status_novo as StatusNovo,
    so.nome as SetorOrigem,
    sd.nome as SetorDestino,
    t.usuario_nome as UsuarioNome,
    t.despacho,
    t.observacao,
    t.justificativa,
    t.data_tramitacao as DataTramitacao
from ged.protocolo_tramitacao t
left join ged.protocolo_setor so on so.id = t.setor_origem_id
left join ged.protocolo_setor sd on sd.id = t.setor_destino_id
where t.tenant_id = @tenantId and t.protocolo_id = @id
order by t.data_tramitacao desc;",
            new { tenantId = TenantId, id })).ToList();

        header.Observacoes = (await db.QueryAsync<ProtocoloObservacaoVM>(@"
select
    o.id,
    o.tipo,
    o.observacao,
    coalesce(s.nome, '') as Setor,
    coalesce(o.usuario_nome, '') as UsuarioNome,
    o.created_at as CreatedAt
from ged.protocolo_observacao o
left join ged.protocolo_setor s on s.id = o.setor_id
where o.tenant_id = @tenantId
  and o.protocolo_id = @id
  and o.reg_status = 'A'
  and (
        o.tipo <> 'INTERNA_SETOR'
        or @admin = true
        or o.setor_id = any(@setorIds)
      )
order by o.created_at desc;",
            new { tenantId = TenantId, id, admin = IsAdminLike(), setorIds = userSetores })).ToList();

        header.SetoresDestino = await LoadSelectAsync(db,
            @"select id, nome from ged.protocolo_setor
              where tenant_id=@tenantId and reg_status='A' and ativo=true
                and id <> @setorAtualId
              order by nome;",
            new { tenantId = TenantId, setorAtualId = header.SetorAtualId });

        header.TiposDocumento = await LoadSelectAsync(db,
            @"select id, nome from ged.protocolo_tipo_documento
              where tenant_id=@tenantId and reg_status='A' and ativo=true
              order by nome;",
            new { tenantId = TenantId });

        return View(header);
    }

    // ==========================================================
    // ANEXAR VÁRIOS DOCUMENTOS
    // Regra: setor atual pode anexar.
    // O dono do anexo é o setor atual no momento do upload.
    // ==========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1024L * 1024L * 200L)]
    public async Task<IActionResult> AnexarDocumentos(Guid protocoloId, Guid? tipoDocumentoId, string? descricao)
    {
        using var db = await OpenAsync();

        var setorAtualId = await GetSetorAtualIdAsync(db, protocoloId);
        if (setorAtualId is null || !await PodeAtuarNoSetorAtualAsync(db, protocoloId))
        {
            TempData["erro"] = "Somente o setor atual do protocolo pode anexar documentos.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();

        try
        {
            var total = await SaveUploadedFilesAsync(db, tx, protocoloId, setorAtualId.Value, Request.Form.Files, tipoDocumentoId, descricao);

            if (total > 0)
            {
                await InsertTramitacaoAsync(db, tx, protocoloId, setorAtualId, setorAtualId, "ANEXO", null, null,
                    $"{total} documento(s) anexado(s).", descricao, null);

                await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo_documento", protocoloId, "ANEXAR_DOCUMENTOS",
                    null, new { total, setorAtualId, tipoDocumentoId, descricao });
            }

            tx.Commit();
            TempData["ok"] = total > 0 ? $"{total} documento(s) anexado(s)." : "Nenhum arquivo foi selecionado.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Erro ao anexar documentos ao protocolo {ProtocoloId}", protocoloId);
            TempData["erro"] = "Erro ao anexar documentos.";
        }

        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    // ==========================================================
    // EXCLUIR ANEXO
    // Regra: somente o setor que anexou pode excluir.
    // ==========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirDocumento(Guid id, Guid protocoloId, string? motivo)
    {
        using var db = await OpenAsync();

        var doc = await db.QueryFirstOrDefaultAsync<DocumentoOwnerRow>(@"
select id as Id, protocolo_id as ProtocoloId, setor_id as SetorId, nome_arquivo as NomeArquivo
from ged.protocolo_documento
where tenant_id=@tenantId and id=@id and protocolo_id=@protocoloId and reg_status='A';",
            new { tenantId = TenantId, id, protocoloId });

        if (doc is null)
        {
            TempData["erro"] = "Documento não encontrado.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        var userSetores = await GetUserSetorIdsAsync(db);
        if (!IsAdminLike() && (!doc.SetorId.HasValue || !userSetores.Contains(doc.SetorId.Value)))
        {
            TempData["erro"] = "Somente o setor que anexou este arquivo pode excluí-lo.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        if (!await PodeVisualizarAsync(db, protocoloId))
        {
            TempData["erro"] = "Você não tem permissão para este protocolo.";
            return RedirectToAction(nameof(Index));
        }

        using var tx = db.BeginTransaction();

        try
        {
            await db.ExecuteAsync(@"
update ged.protocolo_documento
set reg_status='E',
    deleted_at=now(),
    deleted_by=@userId,
    deleted_by_nome=@userName,
    delete_motivo=@motivo
where tenant_id=@tenantId and id=@id;",
                new
                {
                    tenantId = TenantId,
                    id,
                    userId = UserId,
                    userName = UserNameSafe,
                    motivo
                },
                tx);

            await InsertTramitacaoAsync(db, tx, protocoloId, doc.SetorId, doc.SetorId, "EXCLUSAO_ANEXO", null, null,
                $"Documento removido: {doc.NomeArquivo}.", motivo, null);

            await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo_documento", id, "EXCLUIR_DOCUMENTO",
                new { doc.NomeArquivo, doc.SetorId }, new { motivo });

            tx.Commit();

            TempData["ok"] = "Documento removido do protocolo.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Erro ao excluir documento {DocumentoId}", id);
            TempData["erro"] = "Erro ao excluir documento.";
        }

        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [HttpGet]
    public async Task<IActionResult> VisualizarDocumento(Guid id)
    {
        using var db = await OpenAsync();

        var doc = await db.QueryFirstOrDefaultAsync<DocumentoDownloadRow>(@"
select
    d.id,
    d.protocolo_id as ProtocoloId,
    d.nome_arquivo as NomeArquivo,
    d.content_type as ContentType,
    d.arquivo_bytes as ArquivoBytes
from ged.protocolo_documento d
where d.tenant_id=@tenantId and d.id=@id and d.reg_status='A';",
            new { tenantId = TenantId, id });

        if (doc is null || doc.ArquivoBytes is null || doc.ArquivoBytes.Length == 0)
            return NotFound();

        if (!await PodeVisualizarAsync(db, doc.ProtocoloId))
            return Forbid();

        await db.ExecuteAsync(@"
insert into ged.protocolo_auditoria
(tenant_id, protocolo_id, entidade, entidade_id, acao, usuario_id, usuario_nome, ip, user_agent)
values
(@tenantId, @protocoloId, 'protocolo_documento', @documentoId, 'VISUALIZAR_DOCUMENTO', @userId, @userName, @ip, @ua);",
            new
            {
                tenantId = TenantId,
                protocoloId = doc.ProtocoloId,
                documentoId = id,
                userId = UserId,
                userName = UserNameSafe,
                ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                ua = Request.Headers.UserAgent.ToString()
            });

        var contentType = string.IsNullOrWhiteSpace(doc.ContentType) ? "application/octet-stream" : doc.ContentType;
        return File(doc.ArquivoBytes, contentType, doc.NomeArquivo, enableRangeProcessing: true);
    }

    // ==========================================================
    // OBSERVAÇÃO
    // Regra: somente setor atual pode observar/adicionar andamento.
    // ==========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdicionarObservacao(Guid protocoloId, string tipo, string observacao)
    {
        using var db = await OpenAsync();

        var setorAtualId = await GetSetorAtualIdAsync(db, protocoloId);
        if (setorAtualId is null || !await PodeAtuarNoSetorAtualAsync(db, protocoloId))
        {
            TempData["erro"] = "Somente o setor atual do protocolo pode adicionar observação.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        if (string.IsNullOrWhiteSpace(observacao))
        {
            TempData["erro"] = "Informe a observação.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        tipo = string.IsNullOrWhiteSpace(tipo) ? "PUBLICA" : tipo.Trim().ToUpperInvariant();

        using var tx = db.BeginTransaction();

        try
        {
            await InsertObservacaoAsync(db, tx, protocoloId, setorAtualId.Value, tipo, observacao);

            await InsertTramitacaoAsync(db, tx, protocoloId, setorAtualId, setorAtualId, "OBSERVACAO", null, null,
                "Observação adicionada.", observacao, null);

            await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo_observacao", protocoloId, "ADICIONAR_OBSERVACAO",
                null, new { tipo, observacao, setorAtualId });

            tx.Commit();

            TempData["ok"] = "Observação adicionada.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Erro ao adicionar observação.");
            TempData["erro"] = "Erro ao adicionar observação.";
        }

        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    // ==========================================================
    // TRAMITAR
    // Regra: setor atual tramita para o setor destino.
    // Após tramitar, o processo deixa de aparecer no grid do setor antigo.
    // O setor antigo vira participante apenas histórico.
    // Permite anexar vários documentos durante a tramitação.
    // ==========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1024L * 1024L * 200L)]
    public async Task<IActionResult> Tramitar(Guid protocoloId, Guid setorDestinoId, string despacho, string? observacao, Guid? tipoDocumentoId)
    {
        using var db = await OpenAsync();

        if (setorDestinoId == Guid.Empty)
        {
            TempData["erro"] = "Informe o setor de destino.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        var protocolo = await db.QueryFirstOrDefaultAsync<ProtocoloStatusRow>(@"
select id, status, setor_atual_id as SetorAtualId
from ged.protocolo
where tenant_id=@tenantId and id=@protocoloId and reg_status='A';",
            new { tenantId = TenantId, protocoloId });

        if (protocolo is null)
        {
            TempData["erro"] = "Protocolo não encontrado.";
            return RedirectToAction(nameof(Index));
        }

        if (!IsStatusOperacional(protocolo.Status))
        {
            TempData["erro"] = "Este protocolo não pode mais ser tramitado.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        if (!await PodeAtuarNoSetorAtualAsync(db, protocoloId))
        {
            TempData["erro"] = "Somente o setor atual do protocolo pode tramitar.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        if (protocolo.SetorAtualId == setorDestinoId)
        {
            TempData["erro"] = "O setor destino deve ser diferente do setor atual.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();

        try
        {
            // Arquivos anexados no momento da tramitação pertencem ao setor que está tramitando.
            if (protocolo.SetorAtualId.HasValue)
            {
                await SaveUploadedFilesAsync(db, tx, protocoloId, protocolo.SetorAtualId.Value, Request.Form.Files, tipoDocumentoId,
                    "Anexo incluído durante a tramitação.");
            }

            await db.ExecuteAsync(@"
update ged.protocolo
set setor_atual_id=@setorDestinoId,
    status='EM_TRAMITACAO',
    updated_at=now(),
    updated_by=@userId
where tenant_id=@tenantId and id=@protocoloId;",
                new
                {
                    tenantId = TenantId,
                    protocoloId,
                    setorDestinoId,
                    userId = UserId
                },
                tx);

            if (protocolo.SetorAtualId.HasValue)
                await UpsertParticipanteAsync(db, tx, protocoloId, protocolo.SetorAtualId.Value, true, false);

            await UpsertParticipanteAsync(db, tx, protocoloId, setorDestinoId, true, true);

            await InsertTramitacaoAsync(db, tx,
                protocoloId,
                protocolo.SetorAtualId,
                setorDestinoId,
                "TRAMITACAO",
                protocolo.Status,
                "EM_TRAMITACAO",
                despacho,
                observacao,
                null);

            if (!string.IsNullOrWhiteSpace(observacao) && protocolo.SetorAtualId.HasValue)
                await InsertObservacaoAsync(db, tx, protocoloId, protocolo.SetorAtualId.Value, "PUBLICA", observacao);

            await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo", protocoloId, "TRAMITAR",
                new { protocolo.Status, protocolo.SetorAtualId },
                new { Status = "EM_TRAMITACAO", SetorAtualId = setorDestinoId, despacho, observacao });

            tx.Commit();

            TempData["ok"] = "Protocolo tramitado com sucesso. Ele não aparecerá mais na caixa do setor anterior.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Erro ao tramitar protocolo.");
            TempData["erro"] = "Erro ao tramitar protocolo.";
        }

        return RedirectToAction(nameof(Index));
    }

    // ==========================================================
    // FINALIZAR
    // Regra: exige justificativa.
    // ==========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalizar(Guid protocoloId, string justificativa)
    {
        return await EncerrarAsync(protocoloId, "FINALIZADO", "FINALIZACAO", justificativa);
    }

    // ==========================================================
    // ARQUIVAR
    // Regra: exige justificativa.
    // ==========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Arquivar(Guid protocoloId, string justificativa)
    {
        return await EncerrarAsync(protocoloId, "ARQUIVADO", "ARQUIVAMENTO", justificativa);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deferir(Guid protocoloId, string justificativa)
    {
        return await EncerrarAsync(protocoloId, "DEFERIDO", "DEFERIMENTO", justificativa);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Indeferir(Guid protocoloId, string justificativa)
    {
        return await EncerrarAsync(protocoloId, "INDEFERIDO", "INDEFERIMENTO", justificativa);
    }

    private async Task<IActionResult> EncerrarAsync(Guid protocoloId, string statusNovo, string acao, string justificativa)
    {
        using var db = await OpenAsync();

        if (string.IsNullOrWhiteSpace(justificativa))
        {
            TempData["erro"] = "Informe a justificativa.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        var protocolo = await db.QueryFirstOrDefaultAsync<ProtocoloStatusRow>(@"
select id, status, setor_atual_id as SetorAtualId
from ged.protocolo
where tenant_id=@tenantId and id=@protocoloId and reg_status='A';",
            new { tenantId = TenantId, protocoloId });

        if (protocolo is null)
        {
            TempData["erro"] = "Protocolo não encontrado.";
            return RedirectToAction(nameof(Index));
        }

        if (!await PodeAtuarNoSetorAtualAsync(db, protocoloId))
        {
            TempData["erro"] = "Somente o setor atual pode finalizar ou arquivar este protocolo.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();

        try
        {
            await db.ExecuteAsync(@"
update ged.protocolo
set status=@statusNovo,
    data_encerramento=now(),
    justificativa_encerramento=@justificativa,
    updated_at=now(),
    updated_by=@userId
where tenant_id=@tenantId and id=@protocoloId;",
                new
                {
                    tenantId = TenantId,
                    protocoloId,
                    statusNovo,
                    justificativa,
                    userId = UserId
                },
                tx);

            await InsertTramitacaoAsync(db, tx,
                protocoloId,
                protocolo.SetorAtualId,
                protocolo.SetorAtualId,
                acao,
                protocolo.Status,
                statusNovo,
                null,
                null,
                justificativa);

            await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo", protocoloId, acao,
                new { protocolo.Status },
                new { Status = statusNovo, justificativa });

            tx.Commit();

            TempData["ok"] = $"Protocolo {statusNovo.ToLowerInvariant()} com sucesso.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Erro ao encerrar protocolo.");
            TempData["erro"] = "Erro ao encerrar protocolo.";
        }

        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    // ==========================================================
    // Helpers
    // ==========================================================

    private bool IsAdminLike()
    {
        return User.IsInRole(AppRoles.Admin)
               || User.IsInRole(AppRoles.Gestor)
               || User.IsInRole(AppRoles.Auditor);
    }

    private static bool IsStatusOperacional(string? status)
    {
        status = (status ?? "").Trim().ToUpperInvariant();
        return status is "RASCUNHO" or "ABERTO" or "EM_TRAMITACAO";
    }

    private async Task<Guid[]> GetUserSetorIdsAsync(IDbConnection db)
    {
        if (!UserId.HasValue)
            return Array.Empty<Guid>();

        var ids = await db.QueryAsync<Guid>(@"
select setor_id
from ged.protocolo_usuario_setor
where tenant_id=@tenantId
  and usuario_id=@userId
  and ativo=true
  and reg_status='A';",
            new { tenantId = TenantId, userId = UserId.Value });

        return ids.Distinct().ToArray();
    }

    private async Task<Guid?> GetSetorAtualIdAsync(IDbConnection db, Guid protocoloId)
    {
        return await db.ExecuteScalarAsync<Guid?>(@"
select setor_atual_id
from ged.protocolo
where tenant_id=@tenantId and id=@protocoloId and reg_status='A';",
            new { tenantId = TenantId, protocoloId });
    }

    private async Task<bool> PodeAtuarNoSetorAtualAsync(IDbConnection db, Guid protocoloId)
    {
        if (IsAdminLike())
            return true;

        var setorAtualId = await GetSetorAtualIdAsync(db, protocoloId);
        if (!setorAtualId.HasValue)
            return false;

        var userSetores = await GetUserSetorIdsAsync(db);
        return userSetores.Contains(setorAtualId.Value);
    }

    private async Task<bool> PodeVisualizarAsync(IDbConnection db, Guid protocoloId)
    {
        if (IsAdminLike())
            return true;

        var userSetores = await GetUserSetorIdsAsync(db);
        if (userSetores.Length == 0)
            return false;

        var exists = await db.ExecuteScalarAsync<bool>(@"
select exists (
    select 1
    from ged.protocolo p
    where p.tenant_id=@tenantId
      and p.id=@protocoloId
      and p.reg_status='A'
      and (
            p.setor_atual_id = any(@setorIds)
            or exists (
                select 1
                from ged.protocolo_setor_participante sp
                where sp.tenant_id=p.tenant_id
                  and sp.protocolo_id=p.id
                  and sp.setor_id = any(@setorIds)
                  and sp.pode_visualizar=true
            )
      )
);",
            new { tenantId = TenantId, protocoloId, setorIds = userSetores });

        return exists;
    }

    private async Task PopulateFormListsAsync(IDbConnection db, ProtocoloFormVM vm)
    {
        vm.Tipos = await LoadSelectAsync(db, @"select id, nome from ged.protocolo_tipo where tenant_id=@tenantId and reg_status='A' and ativo=true order by nome;", new { tenantId = TenantId });
        vm.Assuntos = await LoadSelectAsync(db, @"select id, nome from ged.protocolo_assunto where tenant_id=@tenantId and reg_status='A' and ativo=true order by nome;", new { tenantId = TenantId });
        vm.Prioridades = await LoadSelectAsync(db, @"select id, nome from ged.protocolo_prioridade where tenant_id=@tenantId and reg_status='A' and ativo=true order by ordem, nome;", new { tenantId = TenantId });
        vm.CanaisEntrada = await LoadSelectAsync(db, @"select id, nome from ged.protocolo_canal_entrada where tenant_id=@tenantId and reg_status='A' and ativo=true order by nome;", new { tenantId = TenantId });
        vm.Setores = await LoadSelectAsync(db, @"select id, nome from ged.protocolo_setor where tenant_id=@tenantId and reg_status='A' and ativo=true order by nome;", new { tenantId = TenantId });
        vm.TiposDocumento = await LoadSelectAsync(db, @"select id, nome from ged.protocolo_tipo_documento where tenant_id=@tenantId and reg_status='A' and ativo=true order by nome;", new { tenantId = TenantId });
    }

    private static async Task<List<SelectListItem>> LoadSelectAsync(IDbConnection db, string sql, object param)
    {
        var rows = await db.QueryAsync<ProtocoloLookupRow>(sql, param);
        return rows.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nome }).ToList();
    }

    private async Task UpsertParticipanteAsync(IDbConnection db, IDbTransaction tx, Guid protocoloId, Guid setorId, bool podeVisualizar, bool podeEditar)
    {
        await db.ExecuteAsync(@"
insert into ged.protocolo_setor_participante
(tenant_id, protocolo_id, setor_id, pode_visualizar, pode_editar, participou_em)
values
(@tenantId, @protocoloId, @setorId, @podeVisualizar, @podeEditar, now())
on conflict (tenant_id, protocolo_id, setor_id)
do update set
    pode_visualizar = excluded.pode_visualizar,
    pode_editar = excluded.pode_editar,
    participou_em = now();",
            new
            {
                tenantId = TenantId,
                protocoloId,
                setorId,
                podeVisualizar,
                podeEditar
            },
            tx);
    }

    private async Task InsertTramitacaoAsync(
        IDbConnection db,
        IDbTransaction tx,
        Guid protocoloId,
        Guid? setorOrigemId,
        Guid? setorDestinoId,
        string acao,
        string? statusAnterior,
        string? statusNovo,
        string? despacho,
        string? observacao,
        string? justificativa)
    {
        await db.ExecuteAsync(@"
insert into ged.protocolo_tramitacao
(
    tenant_id, protocolo_id, setor_origem_id, setor_destino_id,
    usuario_id, usuario_nome, acao, status_anterior, status_novo,
    despacho, observacao, justificativa, ip, user_agent
)
values
(
    @tenantId, @protocoloId, @setorOrigemId, @setorDestinoId,
    @userId, @userName, @acao, @statusAnterior, @statusNovo,
    @despacho, @observacao, @justificativa, @ip, @ua
);",
            new
            {
                tenantId = TenantId,
                protocoloId,
                setorOrigemId,
                setorDestinoId,
                userId = UserId,
                userName = UserNameSafe,
                acao,
                statusAnterior,
                statusNovo,
                despacho,
                observacao,
                justificativa,
                ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                ua = Request.Headers.UserAgent.ToString()
            },
            tx);
    }

    private async Task InsertObservacaoAsync(
        IDbConnection db,
        IDbTransaction tx,
        Guid protocoloId,
        Guid setorId,
        string tipo,
        string observacao)
    {
        await db.ExecuteAsync(@"
insert into ged.protocolo_observacao
(tenant_id, protocolo_id, setor_id, usuario_id, usuario_nome, tipo, observacao)
values
(@tenantId, @protocoloId, @setorId, @userId, @userName, @tipo, @observacao);",
            new
            {
                tenantId = TenantId,
                protocoloId,
                setorId,
                userId = UserId,
                userName = UserNameSafe,
                tipo,
                observacao
            },
            tx);
    }

    private async Task InsertAuditoriaAsync(
        IDbConnection db,
        IDbTransaction tx,
        Guid protocoloId,
        string entidade,
        Guid entidadeId,
        string acao,
        object? anterior,
        object? novo)
    {
        await db.ExecuteAsync(@"
insert into ged.protocolo_auditoria
(
    tenant_id, protocolo_id, entidade, entidade_id, acao,
    valor_anterior, valor_novo,
    usuario_id, usuario_nome, ip, user_agent
)
values
(
    @tenantId, @protocoloId, @entidade, @entidadeId, @acao,
    cast(@anterior as jsonb), cast(@novo as jsonb),
    @userId, @userName, @ip, @ua
);",
            new
            {
                tenantId = TenantId,
                protocoloId,
                entidade,
                entidadeId,
                acao,
                anterior = anterior is null ? null : System.Text.Json.JsonSerializer.Serialize(anterior),
                novo = novo is null ? null : System.Text.Json.JsonSerializer.Serialize(novo),
                userId = UserId,
                userName = UserNameSafe,
                ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                ua = Request.Headers.UserAgent.ToString()
            },
            tx);
    }

    private async Task<int> SaveUploadedFilesAsync(
        IDbConnection db,
        IDbTransaction tx,
        Guid protocoloId,
        Guid setorDonoId,
        IFormFileCollection files,
        Guid? tipoDocumentoId,
        string? descricao)
    {
        if (files is null || files.Count == 0)
            return 0;

        var total = 0;

        foreach (var file in files)
        {
            if (file is null || file.Length <= 0)
                continue;

            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            await db.ExecuteAsync(@"
insert into ged.protocolo_documento
(
    tenant_id, protocolo_id, nome_arquivo, content_type, tamanho_bytes,
    arquivo_bytes, hash_arquivo, tipo_documento_id, descricao,
    anexado_por, anexado_por_nome, setor_id, created_at, reg_status
)
values
(
    @tenantId, @protocoloId, @nomeArquivo, @contentType, @tamanhoBytes,
    @arquivoBytes, @hashArquivo, @tipoDocumentoId, @descricao,
    @userId, @userName, @setorId, now(), 'A'
);",
                new
                {
                    tenantId = TenantId,
                    protocoloId,
                    nomeArquivo = Path.GetFileName(file.FileName),
                    contentType = file.ContentType,
                    tamanhoBytes = file.Length,
                    arquivoBytes = bytes,
                    hashArquivo = hash,
                    tipoDocumentoId,
                    descricao,
                    userId = UserId,
                    userName = UserNameSafe,
                    setorId = setorDonoId
                },
                tx);

            total++;
        }

        return total;
    }

    private sealed class DocumentoDownloadRow
    {
        public Guid Id { get; set; }
        public Guid ProtocoloId { get; set; }
        public string NomeArquivo { get; set; } = "";
        public string? ContentType { get; set; }
        public byte[]? ArquivoBytes { get; set; }
    }

    private sealed class DocumentoOwnerRow
    {
        public Guid Id { get; set; }
        public Guid ProtocoloId { get; set; }
        public Guid? SetorId { get; set; }
        public string NomeArquivo { get; set; } = "";
    }

    private sealed class ProtocoloStatusRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public Guid? SetorAtualId { get; set; }
    }
}
