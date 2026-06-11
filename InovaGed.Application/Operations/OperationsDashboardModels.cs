namespace InovaGed.Application.Operations;

public sealed class OperationsDashboardVm
{
    public OperationsDashboardFilter Filter { get; set; } = new();
    public IReadOnlyList<OperationsSummaryDto> Summary { get; set; } = Array.Empty<OperationsSummaryDto>();
    public IReadOnlyList<OperationQueueItemDto> CriticalItems { get; set; } = Array.Empty<OperationQueueItemDto>();
    public IReadOnlyList<OperationActionDto> NextActions { get; set; } = Array.Empty<OperationActionDto>();
    public IReadOnlyList<ModuleSchemaStatus> ModuleStatuses { get; set; } = Array.Empty<ModuleSchemaStatus>();
    public bool HasSchemaWarnings => ModuleStatuses.Any(x => !x.IsReady);
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

public sealed class ModuleSchemaStatus
{
    public bool IsReady { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public List<string> MissingTables { get; set; } = new();
    public List<string> MissingColumns { get; set; } = new();
    public string? SuggestedMigration { get; set; }
    public string StatusText { get; set; } = "Configurado";
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
    public bool ModuleReady { get; set; } = true;
    public string? StatusText { get; set; }
    public string ModuleName { get; set; } = "GED";
}

public sealed class OperationActionDto
{
    public string Key { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ButtonText { get; set; } = string.Empty;
    public string Url { get; set; } = "#";
    public string Icon { get; set; } = "bi-lightning-charge";
    public string Severity { get; set; } = "medium";
    public int Priority { get; set; }
}

public sealed class OperationQueuePageDto
{
    public IReadOnlyList<OperationQueueItemDto> Items { get; set; } = Array.Empty<OperationQueueItemDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public bool ModuleReady { get; set; } = true;
    public string ModuleName { get; set; } = string.Empty;
    public string? Message { get; set; }
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
    public string? Classification { get; set; }
    public string? Protocol { get; set; }
    public string? Requester { get; set; }
    public string? Sector { get; set; }
    public string? DestinationSector { get; set; }
    public string? Responsible { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? Error { get; set; }
    public int? Attempts { get; set; }
    public int? Parts { get; set; }
    public int? ExpectedParts { get; set; }
    public string? Ocr { get; set; }
    public int? Score { get; set; }
    public string? PendingIssues { get; set; }
    public string? NextStep { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastAnalyzedAt { get; set; }
    public int? ItemsCount { get; set; }
    public string ActionLabel { get; set; } = "Ver detalhes";
    public string ActionUrl { get; set; } = "#";
    public string Severity { get; set; } = "medium";
}

public interface ITableSchemaGuard
{
    Task<bool> TableExistsAsync(string schema, string table, CancellationToken ct);
    Task<bool> ColumnExistsAsync(string schema, string table, string column, CancellationToken ct);
    Task<ModuleSchemaStatus> GetModuleStatusAsync(string moduleName, CancellationToken ct);
}

public interface IOperationsDashboardService
{
    Task<OperationsDashboardVm> GetSummaryAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetGedQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetOcrQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetPartialDocumentsQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetLoanQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetProtocolQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetQualityQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
    Task<OperationQueuePageDto> GetAlertsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct);
}
