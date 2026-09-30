namespace InovaGed.Application.Ged.Protocols;

/// <summary>
/// Regras puras da custódia. O estado do processo não se confunde com o estado da movimentação.
/// </summary>
public static class ProtocolCustodyRules
{
    public const string Waiting = "AGUARDANDO_RECEBIMENTO";
    public const string Received = "RECEBIDA";
    public const string ReturnPending = "DEVOLUCAO_PENDENTE";
    public const string ReturnConfirmed = "RETORNO_CONFIRMADO";
    public const string Reversed = "ESTORNADA";
    public const string InCustody = "EM_CUSTODIA";
    public const string ClosedCustody = "ENCERRADO";

    public static readonly string[] ClosedProcessStatuses =
        ["FINALIZADO", "ARQUIVADO", "CANCELADO", "DEFERIDO", "INDEFERIDO"];

    public static bool IsClosed(string? status) =>
        ClosedProcessStatuses.Contains((status ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Label(string? situation) => (situation ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        Waiting => "A receber",
        Received => "Recebida",
        ReturnPending => "Devolução pendente",
        ReturnConfirmed => "Retorno confirmado",
        Reversed => "Estornada",
        InCustody => "Sob responsabilidade",
        ClosedCustody => "Processo encerrado",
        "PENDING_RECEIPT" => "A receber",
        "RECEIVED" => "Recebida",
        "RETURN_PENDING" => "Devolução pendente",
        "RETURN_CONFIRMED" => "Retorno confirmado",
        "REVERSED" => "Estornada",
        _ => string.IsNullOrWhiteSpace(situation) ? "Sem movimentação" : situation
    };

    public static bool IsPending(string? situation) =>
        string.Equals(situation, Waiting, StringComparison.OrdinalIgnoreCase)
        || string.Equals(situation, ReturnPending, StringComparison.OrdinalIgnoreCase);

    public static bool IsSameDestinationRepeat(string? activeSituation, Guid? activeDestinationId, Guid newDestinationId) =>
        IsPending(activeSituation) && activeDestinationId == newDestinationId && newDestinationId != Guid.Empty;

    public static string? SecondForwardBlock(string? activeSituation, Guid? activeDestinationId, Guid newDestinationId)
    {
        if (!IsPending(activeSituation)) return null;
        if (activeDestinationId == newDestinationId)
            return null;
        return "Já existe uma movimentação ativa para outra destinação. Receba, devolva ou estorne essa pendência antes de um novo encaminhamento.";
    }

    public static ReceiveVerdict DecideReceive(string? situation, Guid? receivedBy, Guid actorId)
    {
        if (IsPending(situation)) return ReceiveVerdict.Applied;
        if (string.Equals(situation, Received, StringComparison.OrdinalIgnoreCase)
            || string.Equals(situation, ReturnConfirmed, StringComparison.OrdinalIgnoreCase))
            return receivedBy == actorId ? ReceiveVerdict.Idempotent : ReceiveVerdict.Conflict;
        return ReceiveVerdict.Invalid;
    }

    public static string? ReverseBlock(string? lastSituation)
    {
        if (IsPending(lastSituation)) return null;
        if (string.Equals(lastSituation, Received, StringComparison.OrdinalIgnoreCase)
            || string.Equals(lastSituation, ReturnConfirmed, StringComparison.OrdinalIgnoreCase))
            return "A última movimentação já foi recebida. Use a devolução; a entrega anterior permanece no histórico.";
        if (string.Equals(lastSituation, Reversed, StringComparison.OrdinalIgnoreCase))
            return "A última movimentação já foi estornada.";
        return "Não há movimentação de custódia elegível para estorno.";
    }

    public static bool CanAct(bool isAdmin, bool linkedToSector, bool rightGranted) =>
        isAdmin || (linkedToSector && rightGranted);

    public static string RetentionOutcome(int executed, int blocked)
    {
        if (executed <= 0 && blocked <= 0) return "NONE";
        if (executed <= 0) return "BLOCKED";
        if (blocked > 0) return "PARTIAL";
        return "COMPLETE";
    }

    public static string? RetentionCaseStatus(string outcome) => outcome switch
    {
        "COMPLETE" => "EXECUTED",
        "PARTIAL" => "PARTIALLY_EXECUTED",
        _ => null
    };

    public static string AttachmentSummary(int attempted, int stored, int failed)
    {
        if (attempted <= 0) return "Nenhum arquivo informado.";
        if (failed <= 0) return "Arquivos armazenados.";
        if (stored <= 0) return "Nenhum arquivo foi armazenado. A operação não foi concluída com os anexos.";
        return $"{stored} arquivo(s) armazenado(s) e {failed} com falha. A operação não foi concluída por completo.";
    }
}

public enum ReceiveVerdict
{
    Applied,
    Idempotent,
    Conflict,
    Invalid
}

public static class ProtocolProcessTransitions
{
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProtocolStatuses.Requested] = [ProtocolStatuses.InReview, ProtocolStatuses.Cancelled],
        [ProtocolStatuses.InReview] = [ProtocolStatuses.Approved, ProtocolStatuses.ReturnedForAdjustment, ProtocolStatuses.Rejected, ProtocolStatuses.Finished, ProtocolStatuses.Cancelled],
        [ProtocolStatuses.ReturnedForAdjustment] = [ProtocolStatuses.AdjustmentAnswered, ProtocolStatuses.Cancelled],
        [ProtocolStatuses.AdjustmentAnswered] = [ProtocolStatuses.InReview, ProtocolStatuses.Approved, ProtocolStatuses.Rejected, ProtocolStatuses.Cancelled],
        [ProtocolStatuses.Approved] = [ProtocolStatuses.Finished, ProtocolStatuses.InReview, ProtocolStatuses.Cancelled]
    };

    public static bool IsTerminal(string? status) =>
        string.Equals(status, ProtocolStatuses.Rejected, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, ProtocolStatuses.Finished, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, ProtocolStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);

    public static bool Can(string? from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return true;
        return from is not null
            && Allowed.TryGetValue(from, out var next)
            && next.Contains(to, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanReopen(string? from) => IsTerminal(from);
}
