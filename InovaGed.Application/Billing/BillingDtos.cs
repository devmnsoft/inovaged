namespace InovaGed.Application.Billing;

public sealed record BillingFilter(string? Supplier = null, string? SupplierDocument = null, string? Competence = null, string? Status = null, decimal? MinimumAmount = null);
public sealed record BillingKpis(int Extracted, int PendingReview, int Approved, int Divergent, decimal TotalAmount, int WithoutOcr);
public sealed class BillingExtractionDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? DocumentVersionId { get; set; }
    public string DocumentTitle { get; set; } = "";
    public string ExtractionStatus { get; set; } = "PENDING";
    public string? DocumentKind { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierDocument { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceSeries { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? CompetenceMonth { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? IssAmount { get; set; }
    public decimal? InssAmount { get; set; }
    public decimal? PisAmount { get; set; }
    public decimal? CofinsAmount { get; set; }
    public decimal? IrAmount { get; set; }
    public decimal? CsllAmount { get; set; }
    public string? ContractNumber { get; set; }
    public string? PurchaseOrder { get; set; }
    public string? CostCenter { get; set; }
    public string? ServiceDescription { get; set; }
    public decimal? UstQuantity { get; set; }
    public decimal? UstUnitValue { get; set; }
    public decimal Confidence { get; set; }
    public string[] Warnings { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public sealed class BillingReviewInput
{
    public string? SupplierName { get; set; }
    public string? SupplierDocument { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceSeries { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? CompetenceMonth { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public string? ContractNumber { get; set; }
    public string? PurchaseOrder { get; set; }
    public string? CostCenter { get; set; }
    public string? ServiceDescription { get; set; }
    public decimal? UstQuantity { get; set; }
    public decimal? UstUnitValue { get; set; }
    public bool HasDivergence { get; set; }
}

public sealed record BillingExtractionCandidate(Guid DocumentId, Guid? DocumentVersionId, string Text);
