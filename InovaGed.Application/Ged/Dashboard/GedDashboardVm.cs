namespace InovaGed.Application.Ged.Dashboard;

public sealed class GedDashboardVm
{
    public bool PartialFailure { get; set; }
    public List<string> WarningMessages { get; set; } = new();

    public int TotalDocuments { get; set; }
    public int ActiveDocuments { get; set; }
    public int DeletedDocuments { get; set; }
    public int UnclassifiedDocuments { get; set; }
    public int ConfidentialDocuments { get; set; }

    public int OcrPending { get; set; }
    public int OcrProcessing { get; set; }
    public int OcrCompleted { get; set; }
    public int OcrError { get; set; }
    public int OcrCancelled { get; set; }
    public int OcrCompleted24h { get; set; }
    public decimal OcrAverageSeconds { get; set; }

    public int FolderMovesToday { get; set; }
    public int FolderMoves7Days { get; set; }
    public List<RecentDocumentMoveDto> RecentMoves { get; set; } = new();

    public int LoanRequested { get; set; }
    public int LoanApproved { get; set; }
    public int LoanDelivered { get; set; }
    public int LoanReturned { get; set; }
    public int LoanOverdue { get; set; }
    public int LoanCancelled { get; set; }
    public List<RecentLoanRequestDto> RecentLoanRequests { get; set; } = new();
    public List<OverdueLoanDto> OverdueLoans { get; set; } = new();

    public int AccessDenied24h { get; set; }
    public int LockedUsers { get; set; }
    public List<RecentAccessDeniedDto> RecentAccessDenied { get; set; } = new();

    public List<RecentAuditEventDto> RecentAuditEvents { get; set; } = new();

    public int RetentionDue30Days { get; set; }
    public int RetentionExpired { get; set; }

    public List<DashboardMetricSlice> OcrByStatus { get; set; } = new();
    public List<DashboardMetricSlice> LoanByStatus { get; set; } = new();
    public List<DashboardMetricSlice> DocumentBySituation { get; set; } = new();
}

public sealed class DashboardMetricSlice { public string Label { get; set; } = string.Empty; public int Value { get; set; } }
public sealed class RecentDocumentMoveDto { public DateTime? MovedAt { get; set; } public string Document { get; set; } = "-"; public string OriginFolder { get; set; } = "-"; public string DestinationFolder { get; set; } = "-"; public string MovedBy { get; set; } = "-"; public string Justification { get; set; } = "-"; }
public sealed class RecentLoanRequestDto { public DateTime? RequestedAt { get; set; } public string Requester { get; set; } = "-"; public string Document { get; set; } = "-"; public string Status { get; set; } = "-"; }
public sealed class OverdueLoanDto { public string ProtocolNo { get; set; } = "-"; public string Borrower { get; set; } = "-"; public string Document { get; set; } = "-"; public DateTime? DueDate { get; set; } }
public sealed class RecentAccessDeniedDto { public DateTime? EventTime { get; set; } public string UserName { get; set; } = "-"; public string Path { get; set; } = "-"; public string IpAddress { get; set; } = "-"; }
public sealed class RecentAuditEventDto { public DateTime? EventTime { get; set; } public string Action { get; set; } = "-"; public string EntityName { get; set; } = "-"; public string Summary { get; set; } = "-"; public string UserName { get; set; } = "-"; }
