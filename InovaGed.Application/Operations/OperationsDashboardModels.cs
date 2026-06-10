namespace InovaGed.Application.Operations;

public sealed class OperationsDashboardVm
{
    public OperationsDashboardFilter Filter { get; set; } = new();
    public IReadOnlyList<OperationsSummaryDto> Summary { get; set; } = Array.Empty<OperationsSummaryDto>();
    public IReadOnlyList<OperationQueueItemDto> CriticalItems { get; set; } = Array.Empty<OperationQueueItemDto>();
    public IReadOnlyList<OperationActionDto> NextActions { get; set; } = Array.Empty<OperationActionDto>();
    public bool IsGlobalScope { get; set; }
    public bool IsSectorScope { get; set; }
    public bool IsPersonalScope { get; set; }
    public string? ScopeLabel { get; set; }
}

public sealed class OperationsDashboardFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? SectorId { get; set; }
    public string? Sector { get; set; }
    public Guid? ResponsibleId { get; set; }
    public string? PendingType { get; set; }
    public string? Priority { get; set; }
    public string? Status { get; set; }
    public bool OnlyMine { get; set; }
    public bool OnlyOverdue { get; set; }
    public bool OnlyCritical { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class OperationsSummaryDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Severity { get; set; } = "low";
    public string CssClass { get; set; } = "primary";
    public string Url { get; set; } = "#";
    public string ActionLabel { get; set; } = "Ver itens";
}

public sealed class OperationActionDto
{
    public string Key { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ButtonText { get; set; } = string.Empty;
    public string Url { get; set; } = "#";
    public string Severity { get; set; } = "medium";
    public int Priority { get; set; }
}

public sealed class OperationQueuePageDto
{
    public IReadOnlyList<OperationQueueItemDto> Items { get; set; } = Array.Empty<OperationQueueItemDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public string EmptyMessage { get; set; } = "Nenhuma pendência encontrada. Tudo em dia para os filtros selecionados.";
}

public sealed class OperationQueueItemDto
{
    public Guid? Id { get; set; }
    public string Queue { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Folder { get; set; }
    public string? DocumentType { get; set; }
    public string? Protocol { get; set; }
    public string? Requester { get; set; }
    public string? Sector { get; set; }
    public string? Status { get; set; }
    public string? Error { get; set; }
    public int? Attempts { get; set; }
    public int? Parts { get; set; }
    public string? Ocr { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? ItemsCount { get; set; }
    public string ActionLabel { get; set; } = "Ver detalhes";
    public string ActionUrl { get; set; } = "#";
    public string Severity { get; set; } = "medium";
}

public sealed class OperationAlertDto
{
    public string Level { get; set; } = "Baixo";
    public string Message { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = "#";
    public DateTime CreatedAt { get; set; }
    public string? Responsible { get; set; }
    public string CssClass { get; set; } = "info";
}

public interface IOperationsDashboardService
{
    Task<OperationsDashboardVm> GetSummaryAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetGedQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetLoanQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetProtocolQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetAlertsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
}
