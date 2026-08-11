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
        var input = new BillingReviewInput { IssueDate = new(2026, 8, 11), DueDate = new(2026, 8, 10), GrossAmount = 0, CompetenceMonth = "13/2026" };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, true));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(input.SupplierDocument)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(input.DueDate)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(input.CompetenceMonth)));
    }

    [Fact]
    public void Divergence_AllowsIncompleteFiscalFields_ButNotNegativeAmounts()
    {
        var input = new BillingReviewInput { HasDivergence = true, GrossAmount = -1 };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, true));
        Assert.Single(errors);
        Assert.Contains(nameof(input.GrossAmount), errors[0].MemberNames);
    }
}
