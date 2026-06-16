using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class SolicitacaoService : ISolicitacaoService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<SolicitacaoService> _logger;

    public SolicitacaoService(IDbConnectionFactory db, IAuditWriter audit, ILogger<SolicitacaoService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }
    public async Task<Result<Guid>> CriarAsync(Guid tenantId, Guid usuarioId, string? usuarioNome, Guid? setorId, SolicitacaoCreateVM vm, CancellationToken ct)
    {
        try
        {
            var id = Guid.NewGuid();
            await using var con = await _db.OpenAsync(ct);
            var now = DateTime.UtcNow;

            const string sql = @"INSERT INTO ged.solicitacoes(
            id, tenant_id, usuario_id, setor_id, arquivo_id, descricao, status, data_solicitacao, data_atualizacao, reg_status
        ) VALUES(
            @Id, @TenantId, @UsuarioId, @SetorId, @ArquivoId, @Descricao, 'PENDENTE', @Now, @Now, 'A'
        )";

            await con.ExecuteAsync(new CommandDefinition(sql,
                new { Id = id, TenantId = tenantId, UsuarioId = usuarioId, SetorId = setorId, vm.ArquivoId, vm.Descricao, Now = now },
                cancellationToken: ct));

            await InserirHistoricoAsync(con, tenantId, id, usuarioId, usuarioNome, HistoricoSolicitacaoAcao.CRIADO, vm.Descricao, now, ct);

            await _audit.WriteAsync(tenantId, usuarioId, "CREATE", "solicitacoes", id, "Solicitação criada", null, null,
                new { solicitacaoId = id, vm.ArquivoId, vm.Descricao, setorId }, ct);

            return Result<Guid>.Ok(id); // ✅ Retorna Result genérico com Guid
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar solicitação para UsuarioId {UsuarioId}, TenantId {TenantId}", usuarioId, tenantId);
            return Result<Guid>.Fail("EXCEPTION", ex.Message);
        }
    }
    public async Task<IReadOnlyList<SolicitacaoRowDto>> ListarParaUsuarioAsync(Guid tenantId, Guid usuarioId, Guid? setorId, bool isAdmin, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            var sql = @"SELECT s.id, s.usuario_id AS UsuarioId, s.setor_id AS SetorId, s.arquivo_id AS ArquivoId, s.descricao,
                        s.status::text AS Status, s.data_solicitacao AS DataSolicitacao, s.data_atualizacao AS DataAtualizacao, s.admin_id AS AdminId,
                        COALESCE(u.user_name, u.email, u.id::text) AS UsuarioNome, sa.nome AS SetorNome,
                        COALESCE(da.codigo,d.codigo) AS ArquivoCodigo, COALESCE(da.nome_arquivo,d.title) AS ArquivoTitulo,
                        COALESCE(a.user_name, a.email, a.id::text) AS AdminNome
                        FROM ged.solicitacoes s
                        LEFT JOIN ged.documentos d ON d.id=s.arquivo_id AND d.tenant_id=s.tenant_id
                        LEFT JOIN ged.documentos_arquivo da ON da.id=s.arquivo_id AND da.tenant_id=s.tenant_id
                        LEFT JOIN ged.setores sa ON sa.id=s.setor_id AND sa.tenant_id=s.tenant_id
                        LEFT JOIN aspnetusers u ON u.id=s.usuario_id
                        LEFT JOIN aspnetusers a ON a.id=s.admin_id
                        WHERE s.tenant_id=@TenantId AND s.reg_status='A'
                        AND (@IsAdmin OR s.usuario_id=@UsuarioId OR (s.setor_id IS NOT NULL AND s.setor_id=@SetorId))
                        ORDER BY s.data_solicitacao DESC";

            var rows = await con.QueryAsync<SolicitacaoRowDto>(new CommandDefinition(sql,
                new { TenantId = tenantId, UsuarioId = usuarioId, SetorId = setorId, IsAdmin = isAdmin }, cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar solicitações para UsuarioId {UsuarioId}, TenantId {TenantId}", usuarioId, tenantId);
            return new List<SolicitacaoRowDto>();
        }
    }

    public async Task<IReadOnlyList<HistoricoSolicitacaoDto>> HistoricoAsync(Guid tenantId, Guid? usuarioId, Guid? setorId, bool isAdmin, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            var sql = @"SELECT h.id, h.solicitacao_id AS SolicitacaoId, h.usuario_id AS UsuarioId,
                        COALESCE(u.user_name, u.email, u.id::text) AS UsuarioNome,
                        h.acao::text AS Acao, h.comentario, h.data
                        FROM ged.historico_solicitacoes h
                        JOIN ged.solicitacoes s ON s.id=h.solicitacao_id AND s.tenant_id=h.tenant_id
                        LEFT JOIN aspnetusers u ON u.id=h.usuario_id
                        WHERE h.tenant_id=@TenantId AND h.reg_status='A'
                        AND (@IsAdmin OR s.usuario_id=@UsuarioId OR (s.setor_id IS NOT NULL AND s.setor_id=@SetorId))
                        ORDER BY h.data DESC";

            return (await con.QueryAsync<HistoricoSolicitacaoDto>(new CommandDefinition(sql,
                new { TenantId = tenantId, UsuarioId = usuarioId, SetorId = setorId, IsAdmin = isAdmin }, cancellationToken: ct))).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar histórico de solicitações, TenantId {TenantId}", tenantId);
            return new List<HistoricoSolicitacaoDto>();
        }
    }

    public async Task<int> PendentesCountAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            return await con.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM ged.solicitacoes WHERE tenant_id=@TenantId AND reg_status='A' AND status='PENDENTE'",
                new { TenantId = tenantId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao contar solicitações pendentes, TenantId {TenantId}", tenantId);
            return 0;
        }
    }

    public async Task<Result> AtualizarStatusAsync(Guid tenantId, Guid solicitacaoId, Guid adminId, string? adminNome, SolicitacaoUpdateStatusVM vm, CancellationToken ct)
    {
        try
        {
            if (vm.Status == SolicitacaoStatus.PENDENTE)
                return Result.Fail("STATUS", "Status inválido para feedback.");

            await using var con = await _db.OpenAsync(ct);
            var now = DateTime.UtcNow;

            var updated = await con.ExecuteAsync(new CommandDefinition(
                @"UPDATE ged.solicitacoes
                  SET status=@Status::ged.solicitacao_status_enum,
                      data_atualizacao=@Now,
                      admin_id=@AdminId
                  WHERE id=@Id AND tenant_id=@TenantId AND reg_status='A'",
                new { Status = vm.Status.ToString(), Now = now, AdminId = adminId, Id = solicitacaoId, TenantId = tenantId },
                cancellationToken: ct));

            if (updated == 0) return Result.Fail("NOTFOUND", "Solicitação não encontrada.");

            await InserirHistoricoAsync(con, tenantId, solicitacaoId, adminId, adminNome, (HistoricoSolicitacaoAcao)vm.Status, vm.Comentario, now, ct);

            await _audit.WriteAsync(tenantId, adminId, "UPDATE", "solicitacoes", solicitacaoId, "Status atualizado", null, null,
                new { solicitacaoId, status = vm.Status.ToString(), vm.Comentario }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar status da solicitação {SolicitacaoId}, TenantId {TenantId}", solicitacaoId, tenantId);
            return Result.Fail("EXCEPTION", ex.Message);
        }
    }


    public async Task<Result> ExcluirAntigasAsync(Guid tenantId, Guid adminId, string? adminNome, DateTime dataLimiteUtc, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            var ids = (await con.QueryAsync<Guid>(new CommandDefinition(
                @"SELECT id FROM ged.solicitacoes WHERE tenant_id=@TenantId AND reg_status='A' AND data_solicitacao < @DataLimiteUtc",
                new { TenantId = tenantId, DataLimiteUtc = dataLimiteUtc }, cancellationToken: ct))).ToList();

            if (ids.Count == 0)
                return Result.Ok();

            foreach (var id in ids)
            {
                await InserirHistoricoAsync(con, tenantId, id, adminId, adminNome, HistoricoSolicitacaoAcao.AGUARDAR, "Solicitação antiga removida por ADMIN", DateTime.UtcNow, ct);
            }

            await con.ExecuteAsync(new CommandDefinition(
                @"UPDATE ged.solicitacoes SET reg_status='E', data_atualizacao=@Now, admin_id=@AdminId WHERE tenant_id=@TenantId AND reg_status='A' AND data_solicitacao < @DataLimiteUtc",
                new { TenantId = tenantId, DataLimiteUtc = dataLimiteUtc, Now = DateTime.UtcNow, AdminId = adminId }, cancellationToken: ct));

            await _audit.WriteAsync(tenantId, adminId, "DELETE", "solicitacoes", null, "Exclusão de solicitações antigas", null, null,
                new { dataLimiteUtc, total = ids.Count }, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir solicitações antigas. TenantId {TenantId}", tenantId);
            return Result.Fail("EXCEPTION", ex.Message);
        }
    }

    private static Task InserirHistoricoAsync(System.Data.IDbConnection con, Guid tenantId, Guid solicitacaoId, Guid usuarioId, string? usuarioNome, HistoricoSolicitacaoAcao acao, string? comentario, DateTime data, CancellationToken ct)
    {
        return con.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO ged.historico_solicitacoes(
                id, tenant_id, solicitacao_id, usuario_id, usuario_nome, acao, comentario, data, reg_status
              ) VALUES(
                @Id, @TenantId, @SolicitacaoId, @UsuarioId, @UsuarioNome, @Acao::ged.historico_solicitacao_acao_enum,
                @Comentario, @Data, 'A'
              )",
            new
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SolicitacaoId = solicitacaoId,
                UsuarioId = usuarioId,
                UsuarioNome = usuarioNome,
                Acao = acao.ToString(),
                Comentario = comentario,
                Data = data
            },
            cancellationToken: ct));
    }
}