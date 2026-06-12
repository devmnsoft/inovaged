using Xunit;
using Dapper;
using InovaGed.Infrastructure.Ged.Loans;
using InovaGed.Infrastructure.Sql;

namespace InovaGed.Application.Tests.Infrastructure.Sql;

public sealed class LoansSqlTests
{
    [Fact]
    public void GetLoanDetailsSql_IsValid()
    {
        AssertLoanDetailsSqlIsValid(LoanQueries.LoanDetailsHeaderSql, "from ged.loan_request lr", "lr.status::text");
        AssertLoanDetailsSqlIsValid(LoanQueries.LoanDetailsItemsSql, "from ged.loan_request_item i", "order by");
    }

    [Fact]
    public void GetLoanHistorySql_IsValid()
        => AssertLoanDetailsSqlIsValid(LoanQueries.LoanDetailsHistorySql, "from ged.loan_request_history h", "order by h.created_at desc");

    private static void AssertLoanDetailsSqlIsValid(string sql, params string[] expectedFragments)
    {
        var parameters = new DynamicParameters();
        parameters.Add("tenant_id", Guid.NewGuid());
        parameters.Add("loan_id", Guid.NewGuid());
        var result = SqlSafetyValidator.Validate(sql, parameters);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains("@tenant_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@loan_id", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var fragment in expectedFragments) Assert.Contains(fragment, sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lwhere", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iwhere", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("where and", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("and and", sql, StringComparison.OrdinalIgnoreCase);
    }
}
