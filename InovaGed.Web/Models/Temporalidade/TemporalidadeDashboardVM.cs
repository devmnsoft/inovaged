namespace InovaGed.Web.Models.Temporalidade;

public sealed class TemporalidadeDashboardVM
{
    public string? Q { get; set; }
    public string? Status { get; set; }
    public int? DueDays { get; set; }

    public RetentionSummaryVM Summary { get; set; } = new();

    public List<RetentionQueueItemVM> Expired { get; set; } = new();
    public List<RetentionQueueItemVM> DueSoon { get; set; } = new();
}

public sealed class RetentionSummaryVM
{
    public int TotalExpired { get; set; }
    public int TotalDue30 { get; set; }
    public int TotalDue60 { get; set; }
    public int TotalDue90 { get; set; }
    public DateTime? LastCalculatedAt { get; set; }
}

public sealed class RetentionQueueItemVM
{
    public Guid DocumentId { get; set; }
    public string? DocCode { get; set; }
    public string? Title { get; set; }

    public string? ClassificationCode { get; set; }
    public string? ClassificationName { get; set; }

    public DateTime? DueAt { get; set; }
    public string? RetentionStatus { get; set; } // EXPIRED / DUE_SOON / OK etc (ajuste)
    public string? SuggestedDestination { get; set; } // ELIMINAR / TRANSFERIR / REAVALIAR etc (ajuste)
}