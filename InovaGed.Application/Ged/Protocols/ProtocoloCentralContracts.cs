namespace InovaGed.Application.Ged.Protocols;

public sealed class ProtocoloActor
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = "";
    public bool CanSeeAll { get; init; }
    public bool IsFullAdmin { get; init; }
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }
}

public sealed class ProtocoloCentralQuery
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public bool CanSeeAll { get; set; }
    public string Visao { get; set; } = "a_receber";
    public string? Q { get; set; }
    public string? Status { get; set; }
    public string? Tipo { get; set; }
    public Guid? SetorId { get; set; }
    public Guid? ResponsavelId { get; set; }
    public DateTime? De { get; set; }
    public DateTime? Ate { get; set; }
    public bool PrazoVencido { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? SelecionadoId { get; set; }
}

public sealed class ProtocoloSetorOption
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Sigla { get; set; }
    public string Rotulo => string.IsNullOrWhiteSpace(Sigla) ? Nome : $"{Nome} ({Sigla})";
}

public sealed class ProtocoloCentralCounts
{
    public int AReceber { get; set; }
    public int ATramitar { get; set; }
    public int Enviados { get; set; }
    public int Devolucoes { get; set; }
}

public sealed class ProtocoloCentralRow
{
    public Guid Id { get; set; }
    public Guid? MovimentoId { get; set; }
    public string Numero { get; set; } = "";
    public string? CodigoDocumental { get; set; }
    public string? Tipo { get; set; }
    public string? Interessado { get; set; }
    public string? Assunto { get; set; }
    public string? Observacao { get; set; }
    public string Status { get; set; } = "";
    public string? SituacaoMovimentacao { get; set; }
    public string? SituacaoCustodia { get; set; }
    public string SituacaoLabel => ProtocolCustodyRules.Label(SituacaoMovimentacao ?? SituacaoCustodia);
    public string? Setor { get; set; }
    public string? Responsavel { get; set; }
    public DateTime? UltimaMovimentacao { get; set; }
    public DateTime? Prazo { get; set; }
    public bool PrazoVencido { get; set; }
    public Guid? SetorAtualId { get; set; }
    public Guid? MovimentoDestinoId { get; set; }
    public Guid? MovimentoOrigemId { get; set; }
    public bool PodeReceber { get; set; }
    public bool PodeTramitar { get; set; }
    public bool PodeDevolver { get; set; }
    public bool PodeEstornar { get; set; }
    public bool PodeConfirmarRetorno { get; set; }
}

public sealed class ProtocoloCustodiaEvento
{
    public Guid Id { get; set; }
    public string Acao { get; set; } = "";
    public string? Situacao { get; set; }
    public string SituacaoLabel => ProtocolCustodyRules.Label(Situacao);
    public string? Origem { get; set; }
    public string? Destino { get; set; }
    public string? Remetente { get; set; }
    public string? Recebedor { get; set; }
    public DateTime Quando { get; set; }
    public DateTime? RecebidaEm { get; set; }
    public string? Despacho { get; set; }
    public string? Justificativa { get; set; }
    public bool Ativa { get; set; }
}

public sealed class ProtocoloCustodiaFisica
{
    public string Origem { get; set; } = "";
    public string? Documento { get; set; }
    public string? Localizacao { get; set; }
    public string? Detentor { get; set; }
    public DateTime? Previsao { get; set; }
    public DateTime? EntregueEm { get; set; }
    public DateTime? RetornoEm { get; set; }
    public string? Situacao { get; set; }
}

public sealed class ProtocoloCentralDetail
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public string? Assunto { get; set; }
    public string? Descricao { get; set; }
    public string? Interessado { get; set; }
    public string? Tipo { get; set; }
    public string Status { get; set; } = "";
    public string? SituacaoCustodia { get; set; }
    public string? SetorOrigem { get; set; }
    public string? SetorAtual { get; set; }
    public string? SetorDestino { get; set; }
    public string? Remetente { get; set; }
    public string? Destinatario { get; set; }
    public DateTime? EnviadoEm { get; set; }
    public DateTime? RecebidoEm { get; set; }
    public Guid? MovimentoId { get; set; }
    public Guid? SetorAtualId { get; set; }
    public string? SituacaoMovimentacao { get; set; }
    public string SituacaoLabel => ProtocolCustodyRules.Label(SituacaoMovimentacao ?? SituacaoCustodia);
    public bool PodeReceber { get; set; }
    public bool PodeTramitar { get; set; }
    public bool PodeDevolver { get; set; }
    public bool PodeEstornar { get; set; }
    public bool PodeConfirmarRetorno { get; set; }
    public bool ExibirCamposHospitalares { get; set; }
    public List<ProtocoloCustodiaEvento> Eventos { get; set; } = new();
    public List<ProtocoloCustodiaFisica> CustodiaFisica { get; set; } = new();
}

public sealed class ProtocoloCentralPage
{
    public ProtocoloCentralQuery Query { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
    public List<ProtocoloCentralRow> Rows { get; set; } = new();
    public ProtocoloCentralDetail? Selecionado { get; set; }
    public List<ProtocoloSetorOption> Setores { get; set; } = new();
    public ProtocoloCentralCounts Counts { get; set; } = new();
    public string? Error { get; set; }
    public bool SemVinculoSetor { get; set; }
}

public sealed class ProtocoloCommandResult
{
    public bool Success { get; init; }
    public bool Idempotent { get; init; }
    public string Message { get; init; } = "";
    public Guid? MovimentoId { get; init; }
    public static ProtocoloCommandResult Ok(string message, Guid? movimentoId = null, bool idempotent = false) =>
        new() { Success = true, Message = message, MovimentoId = movimentoId, Idempotent = idempotent };
    public static ProtocoloCommandResult Fail(string message) => new() { Success = false, Message = message };
}

public sealed class ProtocoloBatchItemResult
{
    public Guid ProtocoloId { get; init; }
    public string? Numero { get; init; }
    public bool Aplicado { get; init; }
    public bool Impedido { get; init; }
    public string Mensagem { get; init; } = "";
}

public sealed class ProtocoloLoteCommand
{
    public string Acao { get; set; } = "";
    public Guid[] Ids { get; set; } = [];
    public Guid? DestinoSetorId { get; set; }
    public string? Observacao { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime? Prazo { get; set; }
    public Guid? ResponsavelId { get; set; }
    public Dictionary<Guid, string> ObservacaoPorItem { get; set; } = new();
    public Dictionary<Guid, string> EntregueAPorItem { get; set; } = new();
}

public sealed class ProtocoloBatchPreview
{
    public string Acao { get; init; } = "";
    public string AcaoLabel { get; init; } = "";
    public Guid? DestinoSetorId { get; init; }
    public string? Destino { get; init; }
    public string? Responsavel { get; init; }
    public DateTime? Prazo { get; init; }
    public string? Observacao { get; init; }
    public List<ProtocoloBatchItemResult> Itens { get; init; } = new();
    public int Elegiveis => Itens.Count(i => !i.Impedido);
    public int Impedidos => Itens.Count(i => i.Impedido);
}

public sealed record ProtocolBatchOutcome(bool AnyApplied, bool Partial, IReadOnlyList<ProtocolBatchItemOutcome> Items, string Message);

public sealed record ProtocolBatchItemOutcome(Guid ProtocolId, bool Applied, string Message);

public interface IProtocoloCentralService
{
    Task<ProtocoloCentralPage> SearchAsync(ProtocoloCentralQuery query, CancellationToken ct);
    Task<IReadOnlyList<ProtocoloSetorOption>> ListSectorsAsync(Guid tenantId, CancellationToken ct);
    Task<ProtocoloCommandResult> ForwardAsync(ProtocoloActor actor, Guid protocoloId, Guid? documentoId, Guid destinoSetorId, string? despacho, string? observacao, string? idempotencyKey, DateTime? prazo, Guid? responsavelId, string? entregueA, CancellationToken ct, Guid? minutaId = null);
    Task<ProtocoloCommandResult> ReceiveAsync(ProtocoloActor actor, Guid? movimentoId, Guid? protocoloId, CancellationToken ct);
    Task<ProtocoloCommandResult> ReturnAsync(ProtocoloActor actor, Guid protocoloId, string? observacao, string? idempotencyKey, CancellationToken ct);
    Task<ProtocoloCommandResult> ConfirmReturnAsync(ProtocoloActor actor, Guid? movimentoId, Guid? protocoloId, CancellationToken ct);
    Task<ProtocoloCommandResult> ReverseAsync(ProtocoloActor actor, Guid protocoloId, string justificativa, CancellationToken ct);
    Task<ProtocoloCommandResult> ReopenAsync(ProtocoloActor actor, Guid protocoloId, string justificativa, CancellationToken ct);
    Task<ProtocoloCommandResult> CloseAsync(ProtocoloActor actor, Guid protocoloId, string justificativa, string? decisao, CancellationToken ct);
    Task<ProtocoloCommandResult> ArchiveAsync(ProtocoloActor actor, Guid protocoloId, string justificativa, string? localizacaoFisica, CancellationToken ct);
    Task<ProtocoloBatchPreview> PreviewBatchAsync(ProtocoloActor actor, ProtocoloLoteCommand command, CancellationToken ct);
    Task<IReadOnlyList<ProtocoloBatchItemResult>> ExecuteBatchAsync(ProtocoloActor actor, ProtocoloLoteCommand command, CancellationToken ct);
}
