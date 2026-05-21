using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Loans;

public interface ISolicitacaoService
{
    Task<Result<Guid>> CriarAsync(Guid tenantId, Guid usuarioId, string? usuarioNome, Guid? setorId, SolicitacaoCreateVM vm, CancellationToken ct);
    Task<IReadOnlyList<SolicitacaoRowDto>> ListarParaUsuarioAsync(Guid tenantId, Guid usuarioId, Guid? setorId, bool isAdmin, CancellationToken ct);
    Task<IReadOnlyList<HistoricoSolicitacaoDto>> HistoricoAsync(Guid tenantId, Guid? usuarioId, Guid? setorId, bool isAdmin, CancellationToken ct);
    Task<int> PendentesCountAsync(Guid tenantId, CancellationToken ct);
    Task<Result> AtualizarStatusAsync(Guid tenantId, Guid solicitacaoId, Guid adminId, string? adminNome, SolicitacaoUpdateStatusVM vm, CancellationToken ct);
    Task<Result> ExcluirAntigasAsync(Guid tenantId, Guid adminId, string? adminNome, DateTime dataLimiteUtc, CancellationToken ct);
}
