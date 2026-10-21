using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace InovaGed.Application.Billing;

public sealed record BillingFilter(string? Supplier = null, string? SupplierDocument = null, string? Competence = null, string? Status = null, decimal? MinimumAmount = null, int Page = 1, int PageSize = 50, Guid? ResponsibleUserId = null, bool? Overdue = null);
public sealed class BillingKpis
{
    public BillingKpis() { }
    public BillingKpis(int Extracted, int PendingReview, int Approved, int Divergent, decimal TotalAmount, int WithoutOcr)
    {
        this.Extracted = Extracted;
        this.PendingReview = PendingReview;
        this.Approved = Approved;
        this.Divergent = Divergent;
        this.TotalAmount = TotalAmount;
        this.WithoutOcr = WithoutOcr;
    }
    public int Extracted { get; set; }
    public int PendingReview { get; set; }
    public int Approved { get; set; }
    public int Divergent { get; set; }
    public decimal TotalAmount { get; set; }
    public int WithoutOcr { get; set; }
}
public sealed class BillingExtractionDto
{
    public long Revision { get; set; }
    public string? PendingCandidateJson { get; set; }
    public string? ExtractedJson { get; set; }
    public Guid? ResponsibleUserId { get; set; }
    public DateTime? ReviewDueDate { get; set; }
    public int Priority { get; set; }
    public string? DivergenceReason { get; set; }
    public int DuplicateCount { get; set; }
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? DocumentVersionId { get; set; }
    public string DocumentTitle { get; set; } = "";
    public string ProcessingStatus { get; set; } = "EXTRACTED";
    // Legacy storage name: this field represents the human review, independently of settlement.
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
    [System.Text.Json.Serialization.JsonIgnore]
    public string WarningsJson { set => Warnings = System.Text.Json.JsonSerializer.Deserialize<string[]>(value) ?? []; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public sealed class BillingReviewInput : IValidatableObject
{
    [Range(0, long.MaxValue)] public long Revision { get; set; }
    public bool UseCandidate { get; set; }
    public bool ConfirmDuplicate { get; set; }
    public Guid? ResponsibleUserId { get; set; }
    public DateTime? ReviewDueDate { get; set; }
    [Range(0, 3)] public int Priority { get; set; }
    [StringLength(1000)] public string? DivergenceReason { get; set; }
    [Required, StringLength(1000, MinimumLength = 5)] public string Notes { get; set; } = "";

    public Guid Id { get; set; }
    public string DocumentTitle { get; set; } = "";
    [StringLength(200)]
    public string? SupplierName { get; set; }
    [StringLength(18)]
    public string? SupplierDocument { get; set; }
    [StringLength(80)]
    public string? InvoiceNumber { get; set; }
    [StringLength(30)]
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var amount in new[] { (GrossAmount, nameof(GrossAmount)), (NetAmount, nameof(NetAmount)), (TaxAmount, nameof(TaxAmount)), (UstQuantity, nameof(UstQuantity)), (UstUnitValue, nameof(UstUnitValue)) })
            if (amount.Item1 < 0) yield return new ValidationResult("O valor não pode ser negativo.", [amount.Item2]);

        if (DueDate < IssueDate)
            yield return new ValidationResult("O vencimento não pode ser anterior à emissão.", [nameof(DueDate)]);
        if (NetAmount > GrossAmount)
            yield return new ValidationResult("O valor líquido não pode exceder o valor bruto.", [nameof(NetAmount)]);
        if (!string.IsNullOrWhiteSpace(CompetenceMonth) && !Regex.IsMatch(CompetenceMonth, @"^(0[1-9]|1[0-2])/\d{4}$"))
            yield return new ValidationResult("Informe a competência no formato MM/AAAA.", [nameof(CompetenceMonth)]);

        if (HasDivergence && string.IsNullOrWhiteSpace(DivergenceReason))
            yield return new ValidationResult("Informe o motivo da divergência.", [nameof(DivergenceReason)]);
        if (!HasDivergence)
        {
            if (string.IsNullOrWhiteSpace(SupplierName)) yield return new ValidationResult("Informe o fornecedor para aprovar.", [nameof(SupplierName)]);
            if (!BrazilianTaxId.IsValid(SupplierDocument)) yield return new ValidationResult("Informe um CPF ou CNPJ válido para aprovar.", [nameof(SupplierDocument)]);
            if (string.IsNullOrWhiteSpace(InvoiceNumber)) yield return new ValidationResult("Informe o número do documento fiscal para aprovar.", [nameof(InvoiceNumber)]);
            if (GrossAmount is null or <= 0) yield return new ValidationResult("Informe um valor bruto maior que zero para aprovar.", [nameof(GrossAmount)]);
        }
    }
}

public static class BrazilianTaxId
{
    public static bool IsValid(string? value)
    {
        var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is not (11 or 14) || digits.Distinct().Count() == 1) return false;
        var baseLength = digits.Length - 2;
        return CheckDigit(digits, baseLength) == digits[baseLength] - '0' && CheckDigit(digits, baseLength + 1) == digits[baseLength + 1] - '0';
    }

    private static int CheckDigit(string digits, int length)
    {
        var sum = 0;
        if (digits.Length == 11)
            for (var i = 0; i < length; i++) sum += (digits[i] - '0') * (length + 1 - i);
        else
        {
            var weight = length - 7;
            for (var i = 0; i < length; i++) { sum += (digits[i] - '0') * weight--; if (weight == 1) weight = 9; }
        }
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}

public sealed class BillingExtractionCandidate
{
    public BillingExtractionCandidate() { }
    public BillingExtractionCandidate(Guid DocumentId, Guid? DocumentVersionId, string Text)
    {
        this.DocumentId = DocumentId;
        this.DocumentVersionId = DocumentVersionId;
        this.Text = Text;
    }
    public Guid DocumentId { get; set; }
    public Guid? DocumentVersionId { get; set; }
    public string Text { get; set; } = "";
}

public class BillingExtractionRuleInput : IValidatableObject
{
    public Guid Id { get; set; }
    [Required, StringLength(160)] public string Name { get; set; } = "";
    [Required, StringLength(60)] public string DocumentKind { get; set; } = "BILLING_DOCUMENT";
    [Required, StringLength(60)] public string TargetField { get; set; } = "InvoiceNumber";
    [StringLength(200)] public string? Keyword { get; set; }
    [StringLength(1000)] public string? RegexPattern { get; set; }
    [Range(1, 10000)] public int Priority { get; set; } = 100;
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Keyword) && string.IsNullOrWhiteSpace(RegexPattern))
            yield return new ValidationResult("Informe uma palavra-chave ou expressão regular.", [nameof(Keyword), nameof(RegexPattern)]);
        if (!string.IsNullOrWhiteSpace(RegexPattern))
        {
            bool regexIsValid = true;
            try
            {
                _ = new Regex(RegexPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException)
            {
                regexIsValid = false;
            }
            if (!regexIsValid)
                yield return new ValidationResult("A expressão regular é inválida.", [nameof(RegexPattern)]);
        }
    }
}

public sealed class BillingExtractionRuleDto : BillingExtractionRuleInput
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
