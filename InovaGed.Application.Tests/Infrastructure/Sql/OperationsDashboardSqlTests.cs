using Xunit;
using Dapper;
using InovaGed.Infrastructure.Operations;
using InovaGed.Infrastructure.Sql;

namespace InovaGed.Application.Tests.Infrastructure.Sql;

public sealed class OperationsDashboardSqlTests
{
    [Fact]
    public void BuildLoansQueueSql_IsValid()
    {
        var sql = OperationsDashboardService.BuildLoansQueueSqlForTests();
        var result = SqlSafetyValidator.Validate(sql, CommonQueueParameters(), requireAllSqlParameters: false);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains("from ged.loan_request l", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("where l.tenant_id=@TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("l.status::text", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("order by", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("offset @Offset limit @Limit", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAlertsSql_IsValid()
    {
        var sql = OperationsDashboardService.BuildAlertsSqlForTests();
        var result = SqlSafetyValidator.Validate(sql, CommonQueueParameters(), requireAllSqlParameters: false);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains("from ged.ocr_job j", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("from ged.loan_request l", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("from ged.protocol_request p", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("where and", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("and and", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildOcrQueueSql_IsValid()
    {
        var sql = OperationsDashboardService.BuildOcrQueueSqlForTests();
        var result = SqlSafetyValidator.Validate(sql, CommonQueueParameters(), requireAllSqlParameters: false);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains("from ged.document d", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left join lateral", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("j.status::text", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("offset @Offset limit @Limit", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static DynamicParameters CommonQueueParameters()
    {
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", Guid.NewGuid());
        parameters.Add("UserId", Guid.NewGuid());
        parameters.Add("Sector", "Arquivo");
        parameters.Add("Status", null);
        parameters.Add("OnlyOverdue", false);
        parameters.Add("Offset", 0);
        parameters.Add("Limit", 20);
        return parameters;
    }
}
