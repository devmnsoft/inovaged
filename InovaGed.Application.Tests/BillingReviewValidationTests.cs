using System.ComponentModel.DataAnnotations;
using InovaGed.Application.Billing;

namespace InovaGed.Application.Tests;

public sealed class BillingReviewValidationTests
{
    [Theory]
    [InlineData("04.252.011/0001-10")]
    [InlineData("529.982.247-25")]
    public void BrazilianTaxId_AcceptsValidDocuments(string value) => Assert.True(BrazilianTaxId.IsValid(value));

    [Theory]
    [InlineData("11.111.111/1111-11")]
    [InlineData("529.982.247-24")]
    public void BrazilianTaxId_RejectsInvalidDocuments(string value) => Assert.False(BrazilianTaxId.IsValid(value));

    [Fact]
    public void Approval_RequiresFiscalFields_AndConsistentDates()
    {
        var input = new BillingReviewInput { Notes = "Conferência fiscal", IssueDate = new(2026, 8, 11), DueDate = new(2026, 8, 10), GrossAmount = 0, CompetenceMonth = "13/2026" };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, true));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(input.SupplierDocument)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(input.DueDate)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(input.CompetenceMonth)));
    }

    [Fact]
    public void Divergence_AllowsIncompleteFiscalFields_ButNotNegativeAmounts()
    {
        var input = new BillingReviewInput { HasDivergence = true, DivergenceReason = "Campo não identificado", Notes = "Conferência fiscal", GrossAmount = -1 };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, true));
        Assert.Single(errors);
        Assert.Contains(nameof(input.GrossAmount), errors[0].MemberNames);
    }
}

public sealed class BillingChecklistTests
{
    [Fact]
    public void Complete_invoice_is_ready_for_review()
    {
        var checks = BillingChecklist.Evaluate(new BillingExtractionDto
        {
            DocumentKind = "INVOICE",
            SupplierName = "Fornecedor Exemplo",
            SupplierDocument = "04.252.011/0001-10",
            InvoiceNumber = "123",
            GrossAmount = 100,
            NetAmount = 90,
            IssueDate = new DateTime(2026, 8, 1),
            DueDate = new DateTime(2026, 8, 10),
            CompetenceMonth = "08/2026",
            Confidence = 80
        });

        Assert.True(BillingChecklist.CanApprove(checks));
        Assert.Equal("Nota fiscal", BillingChecklist.KindLabel("INVOICE"));
    }

    [Fact]
    public void Missing_fiscal_fields_block_approval()
    {
        var checks = BillingChecklist.Evaluate(new BillingExtractionDto
        {
            DocumentKind = "BILL",
            GrossAmount = 0,
            DueDate = new DateTime(2026, 8, 1),
            IssueDate = new DateTime(2026, 8, 10)
        });

        Assert.False(BillingChecklist.CanApprove(checks));
        Assert.Contains(checks, item => item.Code == "TAX_ID" && !item.Passed);
        Assert.Contains(checks, item => item.Code == "DATES" && !item.Passed);
        Assert.Contains(checks, item => item.Code == "GROSS" && !item.Passed);
    }

    [Fact]
    public void Review_preview_uses_the_edited_fields_and_keeps_the_identified_kind()
    {
        var stored = new BillingExtractionDto
        {
            DocumentKind = "INVOICE",
            Confidence = 80,
            SupplierName = "Antigo",
            GrossAmount = 10
        };
        var input = new BillingReviewInput
        {
            SupplierName = "Fornecedor Exemplo",
            SupplierDocument = "04.252.011/0001-10",
            InvoiceNumber = "123",
            GrossAmount = 100,
            NetAmount = 90,
            IssueDate = new DateTime(2026, 8, 1),
            DueDate = new DateTime(2026, 8, 10),
            CompetenceMonth = "08/2026"
        };

        var checks = BillingChecklist.EvaluateReview(stored, input);

        Assert.True(BillingChecklist.CanApprove(checks));
        Assert.Contains(checks, item => item.Code == "IDENTIFIED" && item.Detail == "Nota fiscal");
        Assert.Contains(checks, item => item.Code == "SUPPLIER" && item.Detail == "Fornecedor Exemplo");
    }
}

public sealed class BillingExtractionRuleValidationTests
{
    [Fact]
    public void Requires_keyword_or_regex()
    {
        var input = new BillingExtractionRuleInput { Name = "Nota", DocumentKind = "INVOICE", TargetField = "InvoiceNumber" };
        Assert.Contains(input.Validate(new(input)), x => x.MemberNames.Contains(nameof(input.Keyword)));
    }

    [Fact]
    public void Rejects_invalid_regex()
    {
        var input = new BillingExtractionRuleInput { Name = "Nota", DocumentKind = "INVOICE", TargetField = "InvoiceNumber", RegexPattern = "(" };
        Assert.Contains(input.Validate(new(input)), x => x.MemberNames.Contains(nameof(input.RegexPattern)));
    }
}
