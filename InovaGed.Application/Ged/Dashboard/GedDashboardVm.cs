namespace InovaGed.Application.Ged.Dashboard;

public sealed class GedDashboardVm
{
    public int TotalDocuments { get; set; }
    public int UnclassifiedDocuments { get; set; }
    public int OcrPending { get; set; }
    public int OcrProcessing { get; set; }
    public int OcrFailed { get; set; }
    public int OcrCompleted24h { get; set; }
    public int PendingLoanRequests { get; set; }
    public int OverdueLoans { get; set; }
    public int FolderMoves24h { get; set; }
    public int RetentionExpired { get; set; }
    public int RetentionDue30Days { get; set; }
    public int AccessDenied24h { get; set; }
    public bool HasPartialFailures { get; set; }
    public List<string> WarningMessages { get; set; } = new();

    public List<DashboardMetricSlice> OcrByStatus { get; set; } = new();
    public List<DashboardMetricSlice> DocumentBySituation { get; set; } = new();
    public List<DashboardMetricSlice> LoanByStatus { get; set; } = new();

    public List<RecentOcrErrorVm> RecentOcrErrors { get; set; } = new();
    public List<RecentMoveVm> RecentMoves { get; set; } = new();
    public List<RecentLoanRequestVm> RecentLoanRequests { get; set; } = new();
    public List<RecentAuditEventVm> RecentAuditEvents { get; set; } = new();
}

public sealed class DashboardMetricSlice { public string Label { get; set; } = string.Empty; public int Value { get; set; } }
public sealed class RecentOcrErrorVm { public DateTime? Date { get; set; } public string Document { get; set; } = string.Empty; public Guid? VersionId { get; set; } public int Attempts { get; set; } public string Error { get; set; } = string.Empty; }
public sealed class RecentMoveVm { public DateTime? Date { get; set; } public string Document { get; set; } = string.Empty; public string OldFolder { get; set; } = string.Empty; public string NewFolder { get; set; } = string.Empty; public string User { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; }
public sealed class RecentLoanRequestVm { public DateTime? Date { get; set; } public string Requester { get; set; } = string.Empty; public string Sector { get; set; } = string.Empty; public string Document { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; }
public sealed class RecentAuditEventVm { public DateTime? Date { get; set; } public string User { get; set; } = string.Empty; public string Resource { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; public string Ip { get; set; } = string.Empty; }
