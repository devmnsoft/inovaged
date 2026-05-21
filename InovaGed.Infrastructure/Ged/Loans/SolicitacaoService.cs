using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Domain.Primitives;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class SolicitacaoService : ISolicitacaoService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;

    public SolicitacaoService(IDbConnectionFactory db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<Guid>> CriarAsync(Guid tenantId, Guid usuarioId, string? usuarioNome, Guid? setorId, SolicitacaoCreateVM vm, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var con = await _db.OpenAsync(ct);
        var now = DateTime.UtcNow;

        const string sql = @"insert into ged.solicitacoes(id, tenant_id, usuario_id, setor_id, arquivo_id, descricao, status, data_solicitacao, data_atualizacao, reg_status)
values(@Id,@TenantId,@UsuarioId,@SetorId,@ArquivoId,@Descricao,'PENDENTE',@Now,@Now,'A')";

        await con.ExecuteAsync(new CommandDefinition(sql, new { Id = id, TenantId = tenantId, UsuarioId = usuarioId, SetorId = setorId, vm.ArquivoId, vm.Descricao, Now = now }, cancellationToken: ct));
        await InserirHistoricoAsync(con, tenantId, id, usuarioId, usuarioNome, HistoricoSolicitacaoAcao.CRIADO, vm.Descricao, now, ct);

        await _audit.WriteAsync(tenantId, usuarioId, "CREATE", "solicitacoes", id, "Solicitação criada", null, null,
            new { solicitacaoId = id, vm.ArquivoId, vm.Descricao, setorId }, ct);

        return Result.Ok(id);
    }

    public async Task<IReadOnlyList<SolicitacaoRowDto>> ListarParaUsuarioAsync(Guid tenantId, Guid usuarioId, Guid? setorId, bool isAdmin, CancellationToken ct)
    {
        await using var con = await _db.OpenAsync(ct);
        var sql = @"select s.id,s.usuario_id UsuarioId,s.setor_id SetorId,s.arquivo_id ArquivoId,s.descricao,
s.status::text Status,s.data_solicitacao DataSolicitacao,s.data_atualizacao DataAtualizacao,s.admin_id AdminId,
coalesce(u.full_name,u.user_name) UsuarioNome, sa.nome SetorNome,
coalesce(da.codigo,d.codigo) ArquivoCodigo, coalesce(da.nome_arquivo,d.title) ArquivoTitulo,
coalesce(a.full_name,a.user_name) AdminNome
from ged.solicitacoes s
left join ged.documentos d on d.id=s.arquivo_id and d.tenant_id=s.tenant_id
left join ged.documentos_arquivo da on da.id=s.arquivo_id and da.tenant_id=s.tenant_id
left join ged.setores sa on sa.id=s.setor_id and sa.tenant_id=s.tenant_id
left join aspnetusers u on u.id=s.usuario_id
left join aspnetusers a on a.id=s.admin_id
where s.tenant_id=@TenantId and s.reg_status='A'
and (@IsAdmin or s.usuario_id=@UsuarioId or (s.setor_id is not null and s.setor_id=@SetorId))
order by s.data_solicitacao desc";

        var rows = await con.QueryAsync<SolicitacaoRowDto>(new CommandDefinition(sql, new { TenantId = tenantId, UsuarioId = usuarioId, SetorId = setorId, IsAdmin = isAdmin }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<HistoricoSolicitacaoDto>> HistoricoAsync(Guid tenantId, Guid? usuarioId, Guid? setorId, bool isAdmin, CancellationToken ct)
    {
        await using var con = await _db.OpenAsync(ct);
        var sql = @"select h.id,h.solicitacao_id SolicitacaoId,h.usuario_id UsuarioId,coalesce(u.full_name,u.user_name) UsuarioNome,
h.acao::text Acao,h.comentario,h.data
from ged.historico_solicitacoes h
join ged.solicitacoes s on s.id=h.solicitacao_id and s.tenant_id=h.tenant_id
left join aspnetusers u on u.id=h.usuario_id
where h.tenant_id=@TenantId and h.reg_status='A'
and (@IsAdmin or s.usuario_id=@UsuarioId or (s.setor_id is not null and s.setor_id=@SetorId))
order by h.data desc";

        return (await con.QueryAsync<HistoricoSolicitacaoDto>(new CommandDefinition(sql, new { TenantId = tenantId, UsuarioId = usuarioId, SetorId = setorId, IsAdmin = isAdmin }, cancellationToken: ct))).ToList();
    }

    public async Task<int> PendentesCountAsync(Guid tenantId, CancellationToken ct)
    {
        await using var con = await _db.OpenAsync(ct);
        return await con.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from ged.solicitacoes where tenant_id=@TenantId and reg_status='A' and status='PENDENTE'", new { TenantId = tenantId }, cancellationToken: ct));
    }

    public async Task<Result> AtualizarStatusAsync(Guid tenantId, Guid solicitacaoId, Guid adminId, string? adminNome, SolicitacaoUpdateStatusVM vm, CancellationToken ct)
    {
        if (vm.Status == SolicitacaoStatus.PENDENTE) return Result.Fail("STATUS", "Status inválido para feedback.");

        await using var con = await _db.OpenAsync(ct);
        var now = DateTime.UtcNow;
        var updated = await con.ExecuteAsync(new CommandDefinition(@"update ged.solicitacoes set status=@Status::ged.solicitacao_status_enum, data_atualizacao=@Now, admin_id=@AdminId
where id=@Id and tenant_id=@TenantId and reg_status='A'", new { Status = vm.Status.ToString(), Now = now, AdminId = adminId, Id = solicitacaoId, TenantId = tenantId }, cancellationToken: ct));
        if (updated == 0) return Result.Fail("NOTFOUND", "Solicitação não encontrada.");

        await InserirHistoricoAsync(con, tenantId, solicitacaoId, adminId, adminNome, (HistoricoSolicitacaoAcao)vm.Status, vm.Comentario, now, ct);
        await _audit.WriteAsync(tenantId, adminId, "UPDATE", "solicitacoes", solicitacaoId, "Status atualizado", null, null,
            new { solicitacaoId, status = vm.Status.ToString(), vm.Comentario }, ct);
        return Result.Ok();
    }

    private static Task InserirHistoricoAsync(System.Data.IDbConnection con, Guid tenantId, Guid solicitacaoId, Guid usuarioId, string? usuarioNome, HistoricoSolicitacaoAcao acao, string? comentario, DateTime data, CancellationToken ct)
    {
        return con.ExecuteAsync(new CommandDefinition(@"insert into ged.historico_solicitacoes(id, tenant_id, solicitacao_id, usuario_id, usuario_nome, acao, comentario, data, reg_status)
values(@Id,@TenantId,@SolicitacaoId,@UsuarioId,@UsuarioNome,@Acao::ged.historico_solicitacao_acao_enum,@Comentario,@Data,'A')",
            new { Id = Guid.NewGuid(), TenantId = tenantId, SolicitacaoId = solicitacaoId, UsuarioId = usuarioId, UsuarioNome = usuarioNome, Acao = acao.ToString(), Comentario = comentario, Data = data }, cancellationToken: ct));
    }
}
