using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ProtocoloCadastrosController : GedControllerBase
{
    private readonly ILogger<ProtocoloCadastrosController> _logger;

    public ProtocoloCadastrosController(
        IDbConnectionFactory dbFactory,
        ILogger<ProtocoloCadastrosController> logger) : base(dbFactory)
    {
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string tipo = "setores", string? q = null)
    {
        tipo = NormalizarTipo(tipo);

        using var db = await OpenAsync();

        var sql = MontarSqlLista(tipo, q);
        var itens = (await db.QueryAsync<ProtocoloCadastroRowVM>(sql, new
        {
            tenantId = TenantId,
            q = $"%{q?.Trim()}%"
        })).ToList();

        return View(new ProtocoloCadastroIndexVM
        {
            Tipo = tipo,
            Titulo = Titulo(tipo),
            Q = q,
            Itens = itens
        });
    }

    [HttpGet]
    public async Task<IActionResult> Novo(string tipo = "setores")
    {
        tipo = NormalizarTipo(tipo);

        var vm = new ProtocoloCadastroFormVM
        {
            Tipo = tipo,
            Ativo = true,
            PermiteMultiplos = true,
            ExigeInteressado = true,
            Cor = tipo == "prioridades" ? "primary" : null
        };

        await CarregarCombosAsync(vm);
        return View("Form", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Novo(ProtocoloCadastroFormVM vm)
    {
        vm.Tipo = NormalizarTipo(vm.Tipo);

        if (!ModelState.IsValid)
        {
            await CarregarCombosAsync(vm);
            return View("Form", vm);
        }

        try
        {
            using var db = await OpenAsync();

            var sql = MontarSqlInsert(vm.Tipo);
            await db.ExecuteAsync(sql, new
            {
                tenantId = TenantId,
                vm.Nome,
                vm.Codigo,
                vm.Sigla,
                vm.Descricao,
                vm.Ativo,
                vm.Ordem,
                vm.PrazoDias,
                vm.Cor,
                vm.Obrigatorio,
                vm.PermiteMultiplos,
                vm.ExigeInteressado,
                vm.ExigeDocumentoInicial,
                tipoId = vm.TipoProtocoloId,
                setorPadraoId = vm.SetorPadraoId,
                userId = UserId
            });

            TempData["ok"] = "Cadastro criado com sucesso.";
            return RedirectToAction(nameof(Index), new { tipo = vm.Tipo });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar cadastro básico de protocolo.");
            TempData["erro"] = "Não foi possível criar o cadastro. Verifique se já existe item com o mesmo nome.";
            await CarregarCombosAsync(vm);
            return View("Form", vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Editar(Guid id, string tipo = "setores")
    {
        tipo = NormalizarTipo(tipo);

        using var db = await OpenAsync();

        var sql = MontarSqlGet(tipo);
        var vm = await db.QueryFirstOrDefaultAsync<ProtocoloCadastroFormVM>(sql, new
        {
            tenantId = TenantId,
            id
        });

        if (vm is null)
        {
            TempData["erro"] = "Registro não encontrado.";
            return RedirectToAction(nameof(Index), new { tipo });
        }

        vm.Tipo = tipo;
        await CarregarCombosAsync(vm);
        return View("Form", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(ProtocoloCadastroFormVM vm)
    {
        vm.Tipo = NormalizarTipo(vm.Tipo);

        if (!ModelState.IsValid)
        {
            await CarregarCombosAsync(vm);
            return View("Form", vm);
        }

        try
        {
            using var db = await OpenAsync();

            var sql = MontarSqlUpdate(vm.Tipo);
            await db.ExecuteAsync(sql, new
            {
                tenantId = TenantId,
                vm.Id,
                vm.Nome,
                vm.Codigo,
                vm.Sigla,
                vm.Descricao,
                vm.Ativo,
                vm.Ordem,
                vm.PrazoDias,
                vm.Cor,
                vm.Obrigatorio,
                vm.PermiteMultiplos,
                vm.ExigeInteressado,
                vm.ExigeDocumentoInicial,
                tipoId = vm.TipoProtocoloId,
                setorPadraoId = vm.SetorPadraoId,
                userId = UserId
            });

            TempData["ok"] = "Cadastro atualizado com sucesso.";
            return RedirectToAction(nameof(Index), new { tipo = vm.Tipo });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar cadastro básico de protocolo.");
            TempData["erro"] = "Não foi possível atualizar o cadastro.";
            await CarregarCombosAsync(vm);
            return View("Form", vm);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(Guid id, string tipo = "setores")
    {
        tipo = NormalizarTipo(tipo);

        try
        {
            using var db = await OpenAsync();

            var tabela = Tabela(tipo);
            await db.ExecuteAsync($@"
                update {tabela}
                   set reg_status = 'E',
                       ativo = false,
                       updated_at = now(),
                       updated_by = @userId
                 where tenant_id = @tenantId
                   and id = @id;",
                new
                {
                    tenantId = TenantId,
                    id,
                    userId = UserId
                });

            TempData["ok"] = "Cadastro desativado com sucesso.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir cadastro básico de protocolo.");
            TempData["erro"] = "Não foi possível desativar o cadastro.";
        }

        return RedirectToAction(nameof(Index), new { tipo });
    }

    private async Task CarregarCombosAsync(ProtocoloCadastroFormVM vm)
    {
        using var db = await OpenAsync();

        vm.TiposProtocolo = (await db.QueryAsync<ProtocoloSelectItemVM>(@"
            select id, nome
              from ged.protocolo_tipo
             where tenant_id = @tenantId
               and reg_status = 'A'
               and ativo = true
             order by nome;",
            new { tenantId = TenantId })).ToList();

        vm.Setores = (await db.QueryAsync<ProtocoloSelectItemVM>(@"
            select id, nome
              from ged.protocolo_setor
             where tenant_id = @tenantId
               and reg_status = 'A'
               and ativo = true
             order by nome;",
            new { tenantId = TenantId })).ToList();
    }

    private static string NormalizarTipo(string? tipo)
    {
        tipo = (tipo ?? "setores").Trim().ToLowerInvariant();

        return tipo switch
        {
            "setor" or "setores" => "setores",
            "tipo" or "tipos" => "tipos",
            "assunto" or "assuntos" => "assuntos",
            "prioridade" or "prioridades" => "prioridades",
            "documento" or "tiposdocumento" or "tipos-documento" => "tipos-documento",
            "canal" or "canais" or "canais-entrada" => "canais-entrada",
            "motivo" or "motivos" or "motivos-arquivamento" => "motivos-arquivamento",
            _ => "setores"
        };
    }

    private static string Titulo(string tipo) => tipo switch
    {
        "setores" => "Setores",
        "tipos" => "Tipos de Protocolo",
        "assuntos" => "Assuntos",
        "prioridades" => "Prioridades",
        "tipos-documento" => "Tipos de Documento",
        "canais-entrada" => "Canais de Entrada",
        "motivos-arquivamento" => "Motivos de Arquivamento",
        _ => "Cadastros"
    };

    private static string Tabela(string tipo) => tipo switch
    {
        "setores" => "ged.protocolo_setor",
        "tipos" => "ged.protocolo_tipo",
        "assuntos" => "ged.protocolo_assunto",
        "prioridades" => "ged.protocolo_prioridade",
        "tipos-documento" => "ged.protocolo_tipo_documento",
        "canais-entrada" => "ged.protocolo_canal_entrada",
        "motivos-arquivamento" => "ged.protocolo_motivo_arquivamento",
        _ => "ged.protocolo_setor"
    };

    private static string MontarSqlLista(string tipo, string? q)
    {
        var hasQ = !string.IsNullOrWhiteSpace(q);

        var whereSetor = hasQ
            ? " and (nome ilike @q or coalesce(sigla, '') ilike @q or coalesce(descricao, '') ilike @q) "
            : "";

        var whereComCodigo = hasQ
            ? " and (nome ilike @q or coalesce(codigo, '') ilike @q or coalesce(descricao, '') ilike @q) "
            : "";

        return tipo switch
        {
            "setores" => $@"
                select id, nome, sigla, descricao, ativo
                  from ged.protocolo_setor
                 where tenant_id = @tenantId and reg_status = 'A' {whereSetor}
                 order by nome;",

            "prioridades" => $@"
                select id, nome, codigo, descricao, ativo, ordem, prazo_dias as PrazoDias, cor
                  from ged.protocolo_prioridade
                 where tenant_id = @tenantId and reg_status = 'A' {whereComCodigo}
                 order by ordem, nome;",

            "tipos-documento" => $@"
                select id, nome, codigo, descricao, ativo, obrigatorio, permite_multiplos as PermiteMultiplos
                  from ged.protocolo_tipo_documento
                 where tenant_id = @tenantId and reg_status = 'A' {whereComCodigo}
                 order by nome;",

            "tipos" => $@"
                select id, nome, codigo, descricao, ativo, exige_interessado as ExigeInteressado, exige_documento_inicial as ExigeDocumentoInicial
                  from ged.protocolo_tipo
                 where tenant_id = @tenantId and reg_status = 'A' {whereComCodigo}
                 order by nome;",

            "assuntos" => $@"
                select id, nome, codigo, descricao, ativo, prazo_dias as PrazoDias
                  from ged.protocolo_assunto
                 where tenant_id = @tenantId and reg_status = 'A' {whereComCodigo}
                 order by nome;",

            "canais-entrada" => $@"
                select id, nome, codigo, descricao, ativo
                  from ged.protocolo_canal_entrada
                 where tenant_id = @tenantId and reg_status = 'A' {whereComCodigo}
                 order by nome;",

            "motivos-arquivamento" => $@"
                select id, nome, codigo, descricao, ativo
                  from ged.protocolo_motivo_arquivamento
                 where tenant_id = @tenantId and reg_status = 'A' {whereComCodigo}
                 order by nome;",

            _ => throw new InvalidOperationException("Tipo inválido.")
        };
    }

    private static string MontarSqlGet(string tipo) => tipo switch
    {
        "setores" => @"
            select id, nome, sigla, descricao, ativo
              from ged.protocolo_setor
             where tenant_id = @tenantId and id = @id and reg_status = 'A';",

        "tipos" => @"
            select id, nome, codigo, descricao, ativo,
                   exige_interessado as ExigeInteressado,
                   exige_documento_inicial as ExigeDocumentoInicial
              from ged.protocolo_tipo
             where tenant_id = @tenantId and id = @id and reg_status = 'A';",

        "assuntos" => @"
            select id, nome, codigo, descricao, ativo,
                   prazo_dias as PrazoDias,
                   tipo_id as TipoProtocoloId,
                   setor_padrao_id as SetorPadraoId
              from ged.protocolo_assunto
             where tenant_id = @tenantId and id = @id and reg_status = 'A';",

        "prioridades" => @"
            select id, nome, codigo, descricao, ativo,
                   ordem, prazo_dias as PrazoDias, cor
              from ged.protocolo_prioridade
             where tenant_id = @tenantId and id = @id and reg_status = 'A';",

        "tipos-documento" => @"
            select id, nome, codigo, descricao, ativo,
                   obrigatorio,
                   permite_multiplos as PermiteMultiplos
              from ged.protocolo_tipo_documento
             where tenant_id = @tenantId and id = @id and reg_status = 'A';",

        "canais-entrada" => @"
            select id, nome, codigo, descricao, ativo
              from ged.protocolo_canal_entrada
             where tenant_id = @tenantId and id = @id and reg_status = 'A';",

        "motivos-arquivamento" => @"
            select id, nome, codigo, descricao, ativo
              from ged.protocolo_motivo_arquivamento
             where tenant_id = @tenantId and id = @id and reg_status = 'A';",

        _ => throw new InvalidOperationException("Tipo inválido.")
    };

    private static string MontarSqlInsert(string tipo) => tipo switch
    {
        "setores" => @"
            insert into ged.protocolo_setor
                (tenant_id, nome, sigla, descricao, ativo, created_by)
            values
                (@tenantId, @Nome, @Sigla, @Descricao, @Ativo, @userId);",

        "tipos" => @"
            insert into ged.protocolo_tipo
                (tenant_id, nome, codigo, descricao, exige_interessado, exige_documento_inicial, ativo, created_by)
            values
                (@tenantId, @Nome, @Codigo, @Descricao, @ExigeInteressado, @ExigeDocumentoInicial, @Ativo, @userId);",

        "assuntos" => @"
            insert into ged.protocolo_assunto
                (tenant_id, tipo_id, nome, codigo, descricao, prazo_dias, setor_padrao_id, ativo, created_by)
            values
                (@tenantId, @tipoId, @Nome, @Codigo, @Descricao, @PrazoDias, @setorPadraoId, @Ativo, @userId);",

        "prioridades" => @"
            insert into ged.protocolo_prioridade
                (tenant_id, nome, codigo, descricao, ordem, prazo_dias, cor, ativo, created_by)
            values
                (@tenantId, @Nome, @Codigo, @Descricao, coalesce(@Ordem, 0), @PrazoDias, @Cor, @Ativo, @userId);",

        "tipos-documento" => @"
            insert into ged.protocolo_tipo_documento
                (tenant_id, nome, codigo, descricao, obrigatorio, permite_multiplos, ativo, created_by)
            values
                (@tenantId, @Nome, @Codigo, @Descricao, @Obrigatorio, @PermiteMultiplos, @Ativo, @userId);",

        "canais-entrada" => @"
            insert into ged.protocolo_canal_entrada
                (tenant_id, nome, codigo, descricao, ativo, created_by)
            values
                (@tenantId, @Nome, @Codigo, @Descricao, @Ativo, @userId);",

        "motivos-arquivamento" => @"
            insert into ged.protocolo_motivo_arquivamento
                (tenant_id, nome, codigo, descricao, ativo, created_by)
            values
                (@tenantId, @Nome, @Codigo, @Descricao, @Ativo, @userId);",

        _ => throw new InvalidOperationException("Tipo inválido.")
    };

    private static string MontarSqlUpdate(string tipo) => tipo switch
    {
        "setores" => @"
            update ged.protocolo_setor
               set nome = @Nome,
                   sigla = @Sigla,
                   descricao = @Descricao,
                   ativo = @Ativo,
                   updated_at = now(),
                   updated_by = @userId
             where tenant_id = @tenantId and id = @Id;",

        "tipos" => @"
            update ged.protocolo_tipo
               set nome = @Nome,
                   codigo = @Codigo,
                   descricao = @Descricao,
                   exige_interessado = @ExigeInteressado,
                   exige_documento_inicial = @ExigeDocumentoInicial,
                   ativo = @Ativo,
                   updated_at = now(),
                   updated_by = @userId
             where tenant_id = @tenantId and id = @Id;",

        "assuntos" => @"
            update ged.protocolo_assunto
               set tipo_id = @tipoId,
                   nome = @Nome,
                   codigo = @Codigo,
                   descricao = @Descricao,
                   prazo_dias = @PrazoDias,
                   setor_padrao_id = @setorPadraoId,
                   ativo = @Ativo,
                   updated_at = now(),
                   updated_by = @userId
             where tenant_id = @tenantId and id = @Id;",

        "prioridades" => @"
            update ged.protocolo_prioridade
               set nome = @Nome,
                   codigo = @Codigo,
                   descricao = @Descricao,
                   ordem = coalesce(@Ordem, 0),
                   prazo_dias = @PrazoDias,
                   cor = @Cor,
                   ativo = @Ativo,
                   updated_at = now(),
                   updated_by = @userId
             where tenant_id = @tenantId and id = @Id;",

        "tipos-documento" => @"
            update ged.protocolo_tipo_documento
               set nome = @Nome,
                   codigo = @Codigo,
                   descricao = @Descricao,
                   obrigatorio = @Obrigatorio,
                   permite_multiplos = @PermiteMultiplos,
                   ativo = @Ativo,
                   updated_at = now(),
                   updated_by = @userId
             where tenant_id = @tenantId and id = @Id;",

        "canais-entrada" => @"
            update ged.protocolo_canal_entrada
               set nome = @Nome,
                   codigo = @Codigo,
                   descricao = @Descricao,
                   ativo = @Ativo,
                   updated_at = now(),
                   updated_by = @userId
             where tenant_id = @tenantId and id = @Id;",

        "motivos-arquivamento" => @"
            update ged.protocolo_motivo_arquivamento
               set nome = @Nome,
                   codigo = @Codigo,
                   descricao = @Descricao,
                   ativo = @Ativo,
                   updated_at = now(),
                   updated_by = @userId
             where tenant_id = @tenantId and id = @Id;",

        _ => throw new InvalidOperationException("Tipo inválido.")
    };
}
