namespace InovaGed.Application.Retention;

public sealed class RetentionQueueFilter
{
    public string? Status { get; set; }          // OVERDUE | DUE_SOON | OK | SEM_CLASSIFICACAO
    public DateTimeOffset? DueUntil { get; set; } // filtra retention_due_at <= DueUntil
    public string? Q { get; set; }               // busca textual
}

public sealed class RetentionQueueRow
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }

    // Dados do documento (para exibir na central)
    public string? Code { get; set; }                // ex: DOC-2026-0001 (ou número, protocolo...)
    public string? Title { get; set; }               // título do documento

    // Classificação (PCD / Plano)
    public string? ClassificationCode { get; set; }  // código da classe (ex: 01.02.003)
    public string? ClassificationName { get; set; }  // nome da classe

    // Temporalidade
    public DateTimeOffset? DueAt { get; set; }       // vencimento calculado (nullable)
    public string Status { get; set; } = "OK";       // OVERDUE | DUE_SOON | OK | SEM_CLASSIFICACAO

    // (opcional) Ajuda no filtro e export
    public int? DaysToDue { get; set; }

    public string? ClassCode { get; set; }

    // negativo = vencido

    public DateTimeOffset GeneratedAt { get; set; }
}

public interface IRetentionQueueQueries
{
    Task<IReadOnlyList<RetentionQueueRow>> ListAsync(Guid tenantId, RetentionQueueFilter filter, CancellationToken ct);
    Task<IReadOnlyList<RetentionQueueRow>> ListByIdsAsync(Guid tenantId, Guid[] documentIds, CancellationToken ct);
}