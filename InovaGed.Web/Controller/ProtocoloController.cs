using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ProtocoloController : GedControllerBase
{
    private static readonly string[] StatusFinais = { "DEFERIDO", "INDEFERIDO", "ARQUIVADO", "CANCELADO" };

    public ProtocoloController(IDbConnectionFactory dbFactory) : base(dbFactory)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? status, Guid? setorId)
    {
        using var db = await OpenAsync();
        var setorIds = await GetCurrentUserSetorIdsAsync(db);

        var sql = @"
select
    p.id,
    p.numero as Numero,
    coalesce(tp.nome, '-') as TipoProtocolo,
    p.assunto as Assunto,
    p.interessado as Interessado,
    p.status as Status,
    coalesce(pr.nome, '-') as Prioridade,
    sa.nome as SetorAtual,
    so.nome as SetorOrigem,
    p.created_at as CreatedAt,
    p.data_abertura as DataAbertura
from ged.protocolo p
join ged.protocolo_setor so on so.id = p.setor_origem_id
left join ged.protocolo_setor sa on sa.id = p.setor_atual_id
left join ged.protocolo_tipo tp on tp.id = p.tipo_id
left join ged.protocolo_prioridade pr on pr.id = p.prioridade_id
where p.tenant_id = @tenantId
  and p.reg_status = 'A'
  and (@status is null or @status = '' or p.status = @status)
  and (@q is null or @q = '' or p.assunto ilike '%' || @q || '%' or p.numero ilike '%' || @q || '%' or p.interessado ilike '%' || @q || '%')
  and (@setorId is null or p.setor_atual_id = @setorId)
  and (
        p.criado_por = @userId
        or exists (
            select 1
            from ged.protocolo_setor_participante sp
            where sp.tenant_id = p.tenant_id
              and sp.protocolo_id = p.id
              and sp.pode_visualizar = true
              and sp.setor_id = any(@setorIds)
        )
      )
order by p.created_at desc
limit 500;";

        var vm = new ProtocoloIndexVM
        {
            Q = q,
            Status = status,
            SetorId = setorId,
            Setores = await ListSetoresAsync(db),
            Protocolos = (await db.QueryAsync<ProtocoloListItemVM>(sql, new
            {
                tenantId = TenantId,
                userId = UserId,
                setorIds = setorIds.ToArray(),
                q,
                status,
                setorId
            })).ToList()
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Novo()
    {
        using var db = await OpenAsync();
        var vm = new ProtocoloFormVM();
        await FillFormListsAsync(db, vm);
        var setorPadrao = vm.Setores.FirstOrDefault();
        if (setorPadrao is not null)
        {
            vm.SetorOrigemId = setorPadrao.Id;
            vm.SetorDestinoInicialId = setorPadrao.Id;
        }
        vm.TipoId = vm.Tipos.FirstOrDefault()?.Id;
        vm.CanalEntradaId = vm.CanaisEntrada.FirstOrDefault()?.Id;
        vm.PrioridadeId = vm.Prioridades.FirstOrDefault(x => x.Codigo == "NORMAL")?.Id ?? vm.Prioridades.FirstOrDefault()?.Id;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Novo(ProtocoloFormVM vm, List<IFormFile>? arquivos, Guid? tipoDocumentoId, string? descricaoAnexo)
    {
        using var db = await OpenAsync();
        await FillFormListsAsync(db, vm);

        if (!ModelState.IsValid) return View(vm);

        var id = Guid.NewGuid();
        var now = DateTime.Now;
        var numero = await db.ExecuteScalarAsync<string>("select ged.fn_protocolo_gerar_numero(@tenantId);", new { tenantId = TenantId });
        var setorDestino = vm.SetorDestinoInicialId.HasValue && vm.SetorDestinoInicialId.Value != Guid.Empty
            ? vm.SetorDestinoInicialId.Value
            : vm.SetorOrigemId;

        var enviarParaOutroSetor = !vm.SalvarComoRascunho && setorDestino != vm.SetorOrigemId;
        var status = vm.SalvarComoRascunho
            ? "RASCUNHO"
            : enviarParaOutroSetor ? "EM_TRAMITACAO" : "ABERTO";
        var setorAtual = vm.SalvarComoRascunho ? vm.SetorOrigemId : setorDestino;

        using var tx = db.BeginTransaction();
        try
        {
            await db.ExecuteAsync(@"
insert into ged.protocolo
(id, tenant_id, numero, ano, tipo_id, assunto_id, canal_entrada_id, prioridade_id,
 assunto, interessado, cpf_cnpj, email, telefone, descricao, status,
 setor_origem_id, setor_atual_id, criado_por, criado_por_nome, data_abertura, created_at, reg_status)
values
(@id, @tenantId, @numero, extract(year from now())::integer, @tipoId, @assuntoId, @canalEntradaId, @prioridadeId,
 @assunto, @interessado, @cpfCnpj, @email, @telefone, @descricao, @status,
 @setorOrigemId, @setorAtualId, @userId, @userName, @dataAbertura, @now, 'A');

insert into ged.protocolo_setor_participante
(tenant_id, protocolo_id, setor_id, pode_visualizar, pode_editar, participou_em)
values
(@tenantId, @id, @setorOrigemId, true, @origemEdita, @now)
on conflict (tenant_id, protocolo_id, setor_id)
do update set pode_visualizar = true, pode_editar = excluded.pode_editar, participou_em = excluded.participou_em;

insert into ged.protocolo_setor_participante
(tenant_id, protocolo_id, setor_id, pode_visualizar, pode_editar, participou_em)
values
(@tenantId, @id, @setorAtualId, true, true, @now)
on conflict (tenant_id, protocolo_id, setor_id)
do update set pode_visualizar = true, pode_editar = true, participou_em = excluded.participou_em;

insert into ged.protocolo_usuario_setor
(tenant_id, usuario_id, setor_id, principal, ativo, created_by)
select @tenantId, @userId, @setorOrigemId, true, true, @userId
where @userId is not null
on conflict (tenant_id, usuario_id, setor_id)
do update set ativo = true, reg_status = 'A';", new
            {
                id,
                tenantId = TenantId,
                numero,
                tipoId = vm.TipoId,
                assuntoId = vm.AssuntoId,
                canalEntradaId = vm.CanalEntradaId,
                prioridadeId = vm.PrioridadeId,
                assunto = vm.Assunto,
                interessado = vm.Interessado,
                cpfCnpj = vm.CpfCnpj,
                email = vm.Email,
                telefone = vm.Telefone,
                descricao = vm.Descricao,
                status,
                setorOrigemId = vm.SetorOrigemId,
                setorAtualId = setorAtual,
                origemEdita = vm.SetorOrigemId == setorAtual,
                userId = UserId,
                userName = UserNameSafe,
                dataAbertura = vm.SalvarComoRascunho ? (DateTime?)null : now,
                now
            }, tx);

            await InsertTramitacaoAsync(db, tx, id, vm.SetorOrigemId, vm.SetorOrigemId, "CRIACAO", null, status, null,
                vm.SalvarComoRascunho ? $"Protocolo {numero} criado como rascunho." : $"Protocolo {numero} aberto.");

            if (enviarParaOutroSetor)
            {
                await InsertTramitacaoAsync(db, tx, id, vm.SetorOrigemId, setorDestino, "TRAMITACAO_INICIAL", "ABERTO", "EM_TRAMITACAO",
                    "Encaminhamento inicial definido na abertura do protocolo.", null);
            }

            if (arquivos is not null)
            {
                foreach (var arquivo in arquivos.Where(a => a is not null && a.Length > 0))
                    await SaveDocumentoAsync(db, tx, id, arquivo, tipoDocumentoId, descricaoAnexo, setorAtual);
            }

            await InsertAuditoriaAsync(db, tx, id, "protocolo", id, "CRIAR", null, new
            {
                Numero = numero,
                vm.Assunto,
                vm.Interessado,
                Status = status,
                SetorOrigemId = vm.SetorOrigemId,
                SetorAtualId = setorAtual
            }, setorAtual);

            tx.Commit();
            TempData["ok"] = $"Protocolo {numero} criado com sucesso.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            tx.Rollback();
            TempData["erro"] = "Erro ao criar protocolo: " + ex.Message;
            return View(vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        using var db = await OpenAsync();
        var setorIds = await GetCurrentUserSetorIdsAsync(db);

        if (!await CanViewAsync(db, id, setorIds))
        {
            TempData["erro"] = "Você não possui permissão para visualizar este protocolo.";
            return RedirectToAction(nameof(Index));
        }

        var protocolo = await db.QuerySingleOrDefaultAsync<ProtocoloDetailsVM>(@"
select
    p.id,
    p.numero as Numero,
    coalesce(tp.nome, '-') as TipoProtocolo,
    coalesce(ce.nome, '-') as CanalEntrada,
    coalesce(pr.nome, '-') as Prioridade,
    p.assunto as Assunto,
    p.interessado as Interessado,
    p.cpf_cnpj as CpfCnpj,
    p.email as Email,
    p.telefone as Telefone,
    p.descricao as Descricao,
    p.status as Status,
    p.setor_atual_id as SetorAtualId,
    sa.nome as SetorAtual,
    p.setor_origem_id as SetorOrigemId,
    so.nome as SetorOrigem,
    p.created_at as CreatedAt,
    p.data_abertura as DataAbertura,
    p.data_encerramento as DataEncerramento
from ged.protocolo p
join ged.protocolo_setor so on so.id = p.setor_origem_id
left join ged.protocolo_setor sa on sa.id = p.setor_atual_id
left join ged.protocolo_tipo tp on tp.id = p.tipo_id
left join ged.protocolo_canal_entrada ce on ce.id = p.canal_entrada_id
left join ged.protocolo_prioridade pr on pr.id = p.prioridade_id
where p.tenant_id = @tenantId and p.id = @id and p.reg_status = 'A';", new { tenantId = TenantId, id });

        if (protocolo is null) return NotFound();

        var canAct = await CanActAsync(db, id, setorIds);
        var closed = StatusFinais.Contains(protocolo.Status);

        protocolo.PodeVisualizar = true;
        protocolo.PodeEditar = canAct && !closed;
        protocolo.PodeAnexar = canAct && !closed;
        protocolo.PodeTramitar = canAct && (protocolo.Status == "ABERTO" || protocolo.Status == "EM_TRAMITACAO");
        protocolo.PodeDeferir = protocolo.PodeTramitar;
        protocolo.PodeIndeferir = protocolo.PodeTramitar;
        protocolo.PodeArquivar = canAct && protocolo.Status != "ARQUIVADO" && protocolo.Status != "CANCELADO";
        protocolo.SetoresDestino = (await ListSetoresAsync(db)).Where(x => x.Id != protocolo.SetorAtualId).ToList();
        protocolo.TiposDocumento = await ListTiposDocumentoAsync(db);

        protocolo.Documentos = (await db.QueryAsync<ProtocoloDocumentoVM>(@"
select d.id as Id, d.nome_arquivo as NomeArquivo, d.content_type as ContentType, d.tamanho_bytes as TamanhoBytes,
       coalesce(td.nome, '-') as TipoDocumento, d.descricao as Descricao,
       d.anexado_por_nome as AnexadoPorNome, s.nome as Setor, d.created_at as CreatedAt
from ged.protocolo_documento d
left join ged.protocolo_tipo_documento td on td.id = d.tipo_documento_id
left join ged.protocolo_setor s on s.id = d.setor_id
where d.tenant_id = @tenantId and d.protocolo_id = @id and d.reg_status = 'A'
order by d.created_at desc;", new { tenantId = TenantId, id })).ToList();

        protocolo.Tramitacoes = (await db.QueryAsync<ProtocoloTramitacaoVM>(@"
select t.id as Id, t.acao as Acao, t.status_anterior as StatusAnterior, t.status_novo as StatusNovo,
       so.nome as SetorOrigem, sd.nome as SetorDestino,
       t.usuario_nome as UsuarioNome, t.despacho as Despacho, t.observacao as Observacao, t.data_tramitacao as DataTramitacao
from ged.protocolo_tramitacao t
left join ged.protocolo_setor so on so.id = t.setor_origem_id
left join ged.protocolo_setor sd on sd.id = t.setor_destino_id
where t.tenant_id = @tenantId and t.protocolo_id = @id
order by t.data_tramitacao asc;", new { tenantId = TenantId, id })).ToList();

        protocolo.Observacoes = (await db.QueryAsync<ProtocoloObservacaoVM>(@"
select o.id as Id, o.tipo as Tipo, o.observacao as Observacao, s.nome as Setor, o.usuario_nome as UsuarioNome, o.created_at as CreatedAt
from ged.protocolo_observacao o
left join ged.protocolo_setor s on s.id = o.setor_id
where o.tenant_id = @tenantId
  and o.protocolo_id = @id
  and o.reg_status = 'A'
  and (o.tipo <> 'INTERNA_SETOR' or o.setor_id = any(@setorIds))
order by o.created_at desc;", new { tenantId = TenantId, id, setorIds = setorIds.ToArray() })).ToList();

        return View(protocolo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Protocolar(Guid id)
    {
        using var db = await OpenAsync();
        var setorIds = await GetCurrentUserSetorIdsAsync(db);
        if (!await CanActAsync(db, id, setorIds)) return Forbid();

        var p = await GetProtocoloMinAsync(db, id);
        if (p is null) return NotFound();
        if (p.Status != "RASCUNHO")
        {
            TempData["erro"] = "Somente protocolos em rascunho podem ser protocolados.";
            return RedirectToAction(nameof(Details), new { id });
        }

        using var tx = db.BeginTransaction();
        try
        {
            await db.ExecuteAsync(@"
update ged.protocolo
set status = 'ABERTO', data_abertura = now(), updated_at = now()
where tenant_id = @tenantId and id = @id;", new { tenantId = TenantId, id }, tx);

            await InsertTramitacaoAsync(db, tx, id, p.SetorAtualId, p.SetorAtualId, "PROTOCOLAR", "RASCUNHO", "ABERTO", "Protocolo formalizado.", null);
            await InsertAuditoriaAsync(db, tx, id, "protocolo", id, "PROTOCOLAR", new { p.Status }, new { Status = "ABERTO" }, p.SetorAtualId);
            tx.Commit();
            TempData["ok"] = "Protocolo formalizado com sucesso.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            TempData["erro"] = "Erro ao formalizar protocolo: " + ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnexarDocumento(Guid protocoloId, List<IFormFile>? arquivos, Guid? tipoDocumentoId, string? descricao)
    {
        if (arquivos is null || arquivos.All(a => a is null || a.Length == 0))
        {
            TempData["erro"] = "Selecione pelo menos um arquivo válido.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var db = await OpenAsync();
        var setorIds = await GetCurrentUserSetorIdsAsync(db);
        if (!await CanActAsync(db, protocoloId, setorIds)) return Forbid();

        var p = await GetProtocoloMinAsync(db, protocoloId);
        if (p is null) return NotFound();
        if (StatusFinais.Contains(p.Status))
        {
            TempData["erro"] = "Não é possível anexar documentos em protocolo encerrado.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();
        try
        {
            foreach (var arquivo in arquivos.Where(a => a is not null && a.Length > 0))
                await SaveDocumentoAsync(db, tx, protocoloId, arquivo, tipoDocumentoId, descricao, p.SetorAtualId);

            tx.Commit();
            TempData["ok"] = "Documento(s) anexado(s) com sucesso.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            TempData["erro"] = "Erro ao anexar documento: " + ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [HttpGet]
    public async Task<IActionResult> VisualizarDocumento(Guid id)
    {
        using var db = await OpenAsync();
        var doc = await db.QuerySingleOrDefaultAsync<DocumentoBlob>(@"
select d.id as Id, d.protocolo_id as ProtocoloId, d.nome_arquivo as NomeArquivo, d.content_type as ContentType, d.arquivo_bytes as ArquivoBytes
from ged.protocolo_documento d
where d.tenant_id = @tenantId and d.id = @id and d.reg_status = 'A';", new { tenantId = TenantId, id });

        if (doc is null || doc.ArquivoBytes is null) return NotFound();

        var setorIds = await GetCurrentUserSetorIdsAsync(db);
        if (!await CanViewAsync(db, doc.ProtocoloId, setorIds)) return Forbid();

        var p = await GetProtocoloMinAsync(db, doc.ProtocoloId);
        if (p is not null)
        {
            using var tx = db.BeginTransaction();
            await InsertAuditoriaAsync(db, tx, doc.ProtocoloId, "protocolo_documento", id, "VISUALIZAR_DOCUMENTO", null, new { doc.NomeArquivo }, p.SetorAtualId);
            tx.Commit();
        }

        var contentType = string.IsNullOrWhiteSpace(doc.ContentType) ? "application/octet-stream" : doc.ContentType;
        return File(doc.ArquivoBytes, contentType);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Tramitar(Guid protocoloId, Guid setorDestinoId, string despacho)
    {
        using var db = await OpenAsync();
        var setorIds = await GetCurrentUserSetorIdsAsync(db);
        if (!await CanActAsync(db, protocoloId, setorIds)) return Forbid();

        var p = await GetProtocoloMinAsync(db, protocoloId);
        if (p is null) return NotFound();
        if (p.Status is not ("ABERTO" or "EM_TRAMITACAO"))
        {
            TempData["erro"] = "Este protocolo não pode ser tramitado no status atual.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        if (setorDestinoId == Guid.Empty || setorDestinoId == p.SetorAtualId)
        {
            TempData["erro"] = "Selecione um setor de destino diferente do setor atual.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();
        try
        {
            await db.ExecuteAsync(@"
update ged.protocolo_setor_participante
set pode_editar = false
where tenant_id = @tenantId and protocolo_id = @protocoloId;

insert into ged.protocolo_setor_participante
(tenant_id, protocolo_id, setor_id, pode_visualizar, pode_editar, participou_em)
values
(@tenantId, @protocoloId, @setorDestinoId, true, true, now())
on conflict (tenant_id, protocolo_id, setor_id)
do update set pode_visualizar = true, pode_editar = true, participou_em = now();

update ged.protocolo
set setor_atual_id = @setorDestinoId, status = 'EM_TRAMITACAO', updated_at = now()
where tenant_id = @tenantId and id = @protocoloId;", new { tenantId = TenantId, protocoloId, setorDestinoId }, tx);

            await InsertTramitacaoAsync(db, tx, protocoloId, p.SetorAtualId, setorDestinoId, "TRAMITAR", p.Status, "EM_TRAMITACAO", despacho, null);
            await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo", protocoloId, "TRAMITAR",
                new { p.Status, p.SetorAtualId }, new { Status = "EM_TRAMITACAO", SetorAtualId = setorDestinoId, despacho }, p.SetorAtualId);

            tx.Commit();
            TempData["ok"] = "Protocolo tramitado com sucesso.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            TempData["erro"] = "Erro ao tramitar protocolo: " + ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Deferir(Guid protocoloId, string despacho)
        => DecidirAsync(protocoloId, "DEFERIR", "DEFERIDO", despacho, "Protocolo deferido com sucesso.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Indeferir(Guid protocoloId, string motivo)
        => DecidirAsync(protocoloId, "INDEFERIR", "INDEFERIDO", motivo, "Protocolo indeferido com sucesso.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Arquivar(Guid protocoloId, string motivo)
        => DecidirAsync(protocoloId, "ARQUIVAR", "ARQUIVADO", motivo, "Protocolo arquivado com sucesso.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdicionarObservacao(Guid protocoloId, string tipo, string observacao)
    {
        if (string.IsNullOrWhiteSpace(observacao))
        {
            TempData["erro"] = "Informe a observação.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        tipo = string.IsNullOrWhiteSpace(tipo) ? "PUBLICA" : tipo.ToUpperInvariant();
        if (tipo is not ("PUBLICA" or "INTERNA_SETOR" or "DESPACHO")) tipo = "PUBLICA";

        using var db = await OpenAsync();
        var setorIds = await GetCurrentUserSetorIdsAsync(db);
        if (!await CanActAsync(db, protocoloId, setorIds)) return Forbid();

        var p = await GetProtocoloMinAsync(db, protocoloId);
        if (p is null) return NotFound();
        var obsId = Guid.NewGuid();

        using var tx = db.BeginTransaction();
        try
        {
            await db.ExecuteAsync(@"
insert into ged.protocolo_observacao
(id, tenant_id, protocolo_id, setor_id, usuario_id, usuario_nome, tipo, observacao, created_at, reg_status)
values
(@obsId, @tenantId, @protocoloId, @setorId, @userId, @userName, @tipo, @observacao, now(), 'A');", new
            {
                obsId,
                tenantId = TenantId,
                protocoloId,
                setorId = p.SetorAtualId,
                userId = UserId,
                userName = UserNameSafe,
                tipo,
                observacao
            }, tx);

            await InsertTramitacaoAsync(db, tx, protocoloId, p.SetorAtualId, p.SetorAtualId, "OBSERVACAO", p.Status, p.Status, null,
                tipo == "INTERNA_SETOR" ? "Observação interna do setor adicionada." : "Observação adicionada.");
            await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo_observacao", obsId, "ADICIONAR_OBSERVACAO", null,
                new { tipo, observacao }, p.SetorAtualId);

            tx.Commit();
            TempData["ok"] = "Observação adicionada.";
        }
        catch (Exception ex)
        {
            tx.Rollback();
            TempData["erro"] = "Erro ao adicionar observação: " + ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    private async Task<IActionResult> DecidirAsync(Guid protocoloId, string acao, string statusNovo, string? despacho, string mensagemOk)
    {
        using var db = await OpenAsync();
        var setorIds = await GetCurrentUserSetorIdsAsync(db);
        if (!await CanActAsync(db, protocoloId, setorIds)) return Forbid();

        var p = await GetProtocoloMinAsync(db, protocoloId);
        if (p is null) return NotFound();
        if (p.Status == "ARQUIVADO" || p.Status == "CANCELADO")
        {
            TempData["erro"] = "Este protocolo já está encerrado.";
            return RedirectToAction(nameof(Details), new { id = protocoloId });
        }

        using var tx = db.BeginTransaction();
        try
        {
            var encerramento = statusNovo is "DEFERIDO" or "INDEFERIDO" or "ARQUIVADO";
            await db.ExecuteAsync(@"
update ged.protocolo
set status = @statusNovo,
    motivo_encerramento = @despacho,
    data_encerramento = case when @encerramento then coalesce(data_encerramento, now()) else data_encerramento end,
    updated_at = now()
where tenant_id = @tenantId and id = @protocoloId;", new
            {
                tenantId = TenantId,
                protocoloId,
                statusNovo,
                despacho,
                encerramento
            }, tx);

            await db.ExecuteAsync(@"
update ged.protocolo_setor_participante
set pode_editar = false
where tenant_id = @tenantId and protocolo_id = @protocoloId and @bloquearEdicao = true;", new
            {
                tenantId = TenantId,
                protocoloId,
                bloquearEdicao = StatusFinais.Contains(statusNovo)
            }, tx);

            await InsertTramitacaoAsync(db, tx, protocoloId, p.SetorAtualId, p.SetorAtualId, acao, p.Status, statusNovo, despacho, null);
            await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo", protocoloId, acao,
                new { p.Status }, new { Status = statusNovo, despacho }, p.SetorAtualId);

            tx.Commit();
            TempData["ok"] = mensagemOk;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            TempData["erro"] = "Erro ao executar ação no protocolo: " + ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = protocoloId });
    }

    private async Task FillFormListsAsync(IDbConnection db, ProtocoloFormVM vm)
    {
        vm.Setores = await ListSetoresAsync(db);
        vm.Tipos = await ListOptionsAsync(db, "ged.protocolo_tipo");
        vm.Assuntos = await ListOptionsAsync(db, "ged.protocolo_assunto");
        vm.CanaisEntrada = await ListOptionsAsync(db, "ged.protocolo_canal_entrada");
        vm.Prioridades = await ListOptionsAsync(db, "ged.protocolo_prioridade");
        vm.TiposDocumento = await ListTiposDocumentoAsync(db);
    }

    private async Task<List<ProtocoloOptionVM>> ListOptionsAsync(IDbConnection db, string table)
    {
        var sql = $@"select id as Id, nome as Nome, codigo as Codigo from {table} where tenant_id = @tenantId and ativo = true and reg_status = 'A' order by nome;";
        return (await db.QueryAsync<ProtocoloOptionVM>(sql, new { tenantId = TenantId })).ToList();
    }

    private async Task<List<ProtocoloTipoDocumentoOptionVM>> ListTiposDocumentoAsync(IDbConnection db)
    {
        return (await db.QueryAsync<ProtocoloTipoDocumentoOptionVM>(@"
select id as Id, nome as Nome, obrigatorio as Obrigatorio
from ged.protocolo_tipo_documento
where tenant_id = @tenantId and ativo = true and reg_status = 'A'
order by obrigatorio desc, nome;", new { tenantId = TenantId })).ToList();
    }

    private async Task<List<ProtocoloSetorOptionVM>> ListSetoresAsync(IDbConnection db)
    {
        var setores = await db.QueryAsync<ProtocoloSetorOptionVM>(@"
select id as Id, nome as Nome, sigla as Sigla
from ged.protocolo_setor
where tenant_id = @tenantId and ativo = true and reg_status = 'A'
order by nome;", new { tenantId = TenantId });
        return setores.ToList();
    }

    private async Task<Guid[]> GetCurrentUserSetorIdsAsync(IDbConnection db)
    {
        if (!UserId.HasValue) return Array.Empty<Guid>();
        var setores = await db.QueryAsync<Guid>(@"
select setor_id
from ged.protocolo_usuario_setor
where tenant_id = @tenantId and usuario_id = @userId and ativo = true and reg_status = 'A';", new { tenantId = TenantId, userId = UserId.Value });
        return setores.ToArray();
    }

    private async Task<bool> CanViewAsync(IDbConnection db, Guid protocoloId, Guid[] setorIds)
    {
        return await db.ExecuteScalarAsync<bool>(@"
select exists (
    select 1
    from ged.protocolo p
    where p.tenant_id = @tenantId
      and p.id = @protocoloId
      and p.reg_status = 'A'
      and (
            p.criado_por = @userId
            or exists (
                select 1
                from ged.protocolo_setor_participante sp
                where sp.tenant_id = p.tenant_id
                  and sp.protocolo_id = p.id
                  and sp.pode_visualizar = true
                  and sp.setor_id = any(@setorIds)
            )
      )
);", new { tenantId = TenantId, protocoloId, userId = UserId, setorIds });
    }

    private async Task<bool> CanActAsync(IDbConnection db, Guid protocoloId, Guid[] setorIds)
    {
        return await db.ExecuteScalarAsync<bool>(@"
select exists (
    select 1
    from ged.protocolo p
    where p.tenant_id = @tenantId
      and p.id = @protocoloId
      and p.reg_status = 'A'
      and exists (
            select 1
            from ged.protocolo_setor_participante sp
            where sp.tenant_id = p.tenant_id
              and sp.protocolo_id = p.id
              and sp.pode_editar = true
              and sp.setor_id = any(@setorIds)
      )
);", new { tenantId = TenantId, protocoloId, setorIds });
    }

    private async Task<ProtocoloMin?> GetProtocoloMinAsync(IDbConnection db, Guid id)
    {
        return await db.QuerySingleOrDefaultAsync<ProtocoloMin>(@"
select id as Id, numero as Numero, status as Status, setor_atual_id as SetorAtualId, setor_origem_id as SetorOrigemId
from ged.protocolo
where tenant_id = @tenantId and id = @id and reg_status = 'A';", new { tenantId = TenantId, id });
    }

    private async Task SaveDocumentoAsync(IDbConnection db, IDbTransaction tx, Guid protocoloId, IFormFile arquivo, Guid? tipoDocumentoId, string? descricao, Guid? setorId)
    {
        using var ms = new MemoryStream();
        await arquivo.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var docId = Guid.NewGuid();

        await db.ExecuteAsync(@"
insert into ged.protocolo_documento
(id, tenant_id, protocolo_id, tipo_documento_id, nome_arquivo, content_type, tamanho_bytes, arquivo_bytes, hash_arquivo,
 descricao, anexado_por, anexado_por_nome, setor_id, created_at, reg_status)
values
(@docId, @tenantId, @protocoloId, @tipoDocumentoId, @nomeArquivo, @contentType, @tamanhoBytes, @arquivoBytes, @hashArquivo,
 @descricao, @userId, @userName, @setorId, now(), 'A');", new
        {
            docId,
            tenantId = TenantId,
            protocoloId,
            tipoDocumentoId,
            nomeArquivo = Path.GetFileName(arquivo.FileName),
            contentType = arquivo.ContentType,
            tamanhoBytes = arquivo.Length,
            arquivoBytes = bytes,
            hashArquivo = hash,
            descricao,
            userId = UserId,
            userName = UserNameSafe,
            setorId
        }, tx);

        await InsertTramitacaoAsync(db, tx, protocoloId, setorId, setorId, "ANEXO_DOCUMENTO", null, null, null,
            $"Documento anexado: {arquivo.FileName}");
        await InsertAuditoriaAsync(db, tx, protocoloId, "protocolo_documento", docId, "ANEXAR_DOCUMENTO", null,
            new { arquivo.FileName, arquivo.Length, Hash = hash }, setorId);
    }

    private async Task InsertTramitacaoAsync(IDbConnection db, IDbTransaction tx, Guid protocoloId, Guid? setorOrigemId, Guid? setorDestinoId,
        string acao, string? statusAnterior, string? statusNovo, string? despacho, string? observacao)
    {
        await db.ExecuteAsync(@"
insert into ged.protocolo_tramitacao
(tenant_id, protocolo_id, setor_origem_id, setor_destino_id, usuario_id, usuario_nome, acao, status_anterior, status_novo,
 despacho, observacao, ip, user_agent)
values
(@tenantId, @protocoloId, @setorOrigemId, @setorDestinoId, @userId, @userName, @acao, @statusAnterior, @statusNovo,
 @despacho, @observacao, @ip, @userAgent);", new
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
            ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent = Request.Headers.UserAgent.ToString()
        }, tx);
    }

    private async Task InsertAuditoriaAsync(IDbConnection db, IDbTransaction tx, Guid protocoloId, string entidade, Guid entidadeId,
        string acao, object? anterior, object? novo, Guid? setorId)
    {
        await db.ExecuteAsync(@"
insert into ged.protocolo_auditoria
(tenant_id, protocolo_id, entidade, entidade_id, acao, valor_anterior, valor_novo, usuario_id, usuario_nome, setor_id, ip, user_agent)
values
(@tenantId, @protocoloId, @entidade, @entidadeId, @acao, cast(@valorAnterior as jsonb), cast(@valorNovo as jsonb),
 @userId, @userName, @setorId, @ip, @userAgent);", new
        {
            tenantId = TenantId,
            protocoloId,
            entidade,
            entidadeId,
            acao,
            valorAnterior = anterior is null ? null : JsonSerializer.Serialize(anterior),
            valorNovo = novo is null ? null : JsonSerializer.Serialize(novo),
            userId = UserId,
            userName = UserNameSafe,
            setorId,
            ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent = Request.Headers.UserAgent.ToString()
        }, tx);
    }

    private sealed class ProtocoloMin
    {
        public Guid Id { get; set; }
        public string? Numero { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? SetorAtualId { get; set; }
        public Guid SetorOrigemId { get; set; }
    }

    private sealed class DocumentoBlob
    {
        public Guid Id { get; set; }
        public Guid ProtocoloId { get; set; }
        public string NomeArquivo { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public byte[]? ArquivoBytes { get; set; }
    }
}
