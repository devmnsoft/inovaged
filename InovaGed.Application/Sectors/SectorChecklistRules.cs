namespace InovaGed.Application.Sectors;

public sealed record SectorChecklistOccurrence(
    Guid? LinkId,
    string ReviewStatus,
    string PreviewStatus,
    string OcrStatus,
    string ExtractionStatus,
    string? PhysicalEvidence,
    bool HasReviewNeeded = false);

public static class SectorChecklistRules
{
    private static readonly HashSet<string> ReadyPreviewStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "CONCLUIDO", "PRONTO", "DISPONIVEL", "JA_EXISTENTE"
    };

    public static string EvaluateState(
        bool isRequired,
        bool requiresPhysical,
        IReadOnlyList<SectorChecklistOccurrence> occurrences)
    {
        if (occurrences.Count == 0)
        {
            return isRequired ? "Documento faltante" : "Não exigido neste contexto";
        }

        // 1. Revisão necessária tem prioridade máxima de alerta
        if (occurrences.Any(o => o.HasReviewNeeded ||
                                 string.Equals(o.ReviewStatus, "REVISAO_NECESSARIA", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(o.ReviewStatus, "INCONSISTENTE", StringComparison.OrdinalIgnoreCase)))
        {
            return "Revisão necessária";
        }

        // 2. Original físico pendente
        if (requiresPhysical && occurrences.Any(o => string.IsNullOrWhiteSpace(o.PhysicalEvidence)))
        {
            return "Original físico pendente";
        }

        // 3. Todas marcadas como Não Aplicável
        if (occurrences.All(o => string.Equals(o.ReviewStatus, "NAO_APLICAVEL", StringComparison.OrdinalIgnoreCase)))
        {
            return "Não aplicável com justificativa";
        }

        // 4. Conferido: TODAS as ocorrências devem estar concluídas/conferidas (sem combinar ocorrência conferida com pendente)
        var allConferredOrNotApplicable = occurrences.All(o =>
            string.Equals(o.ReviewStatus, "CONFERIDO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.ReviewStatus, "NAO_APLICAVEL", StringComparison.OrdinalIgnoreCase));

        var hasAtLeastOneConferred = occurrences.Any(o =>
            string.Equals(o.ReviewStatus, "CONFERIDO", StringComparison.OrdinalIgnoreCase));

        if (allConferredOrNotApplicable && hasAtLeastOneConferred)
        {
            return "Documento conferido";
        }

        // 5. Se há itens pendentes de conferência humana, reflete o estágio do pipeline de processamento:
        if (occurrences.Any(o => string.Equals(o.ExtractionStatus, "CONCLUIDO_PARCIAL", StringComparison.OrdinalIgnoreCase)))
        {
            return "Extração parcial";
        }

        if (occurrences.Any(o => string.Equals(o.ExtractionStatus, "CONCLUIDO", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(o.ExtractionStatus, "REVISAO_PENDENTE", StringComparison.OrdinalIgnoreCase)))
        {
            return "Sugestão pendente";
        }

        if (occurrences.Any(o => string.Equals(o.OcrStatus, "CONCLUIDO", StringComparison.OrdinalIgnoreCase)))
        {
            return "OCR concluído";
        }

        if (occurrences.Any(o => ReadyPreviewStates.Contains(o.PreviewStatus)))
        {
            return "Prévia disponível";
        }

        return "Documento recebido";
    }
}
