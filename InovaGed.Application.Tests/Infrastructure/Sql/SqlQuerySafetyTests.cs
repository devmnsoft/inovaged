using Xunit;
using Dapper;
using InovaGed.Infrastructure.Sql;

namespace InovaGed.Application.Tests.Infrastructure.Sql;

public sealed class SqlQuerySafetyTests
{
    [Theory]
    [InlineData("select * from ged.protocol_request pwhere p.tenant_id = @TenantId", "pwhere")]
    [InlineData("select * from ged.document d where and d.tenant_id = @TenantId", "where and")]
    [InlineData("select * from ged.loan_request l where l.tenant_id = @TenantId and and l.reg_status = 'A'", "and and")]
    [InlineData("selectfrom ged.document", "selectfrom")]
    [InlineData("select * fromged.document", "fromged")]
    public void Validate_ForbiddenPatterns_AreInvalid(string sql, string expectedPattern)
    {
        var result = SqlSafetyValidator.Validate(sql, requireAllSqlParameters: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(expectedPattern, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingDynamicParameter_IsInvalid()
    {
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", Guid.NewGuid());

        var result = SqlSafetyValidator.Validate("select * from ged.document d where d.tenant_id = @TenantId and d.id = @DocumentId", parameters);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("@DocumentId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UnusedDynamicParameter_IsWarningOnly()
    {
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", Guid.NewGuid());
        parameters.Add("Unused", 123);

        var result = SqlSafetyValidator.Validate("select * from ged.document d where d.tenant_id = @TenantId", parameters);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("@Unused", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SafeSqlBuilder_AppendsFiltersWithAndAndPagination()
    {
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", Guid.NewGuid());
        parameters.Add("Status", "REQUESTED");
        parameters.Add("Search", "%abc%");
        parameters.Add("Offset", 0);
        parameters.Add("Limit", 20);

        var sql = new SafeSqlBuilder("""
select p.id
from ged.protocol_request p
where p.tenant_id = @TenantId
""")
            .And("coalesce(p.reg_status, 'A') = 'A'")
            .And("upper(p.status::text) = upper(@Status)")
            .And("(p.title ilike @Search or p.protocol_no ilike @Search)")
            .OrderBy("p.requested_at desc")
            .Paginate()
            .ToSql();

        var result = SqlSafetyValidator.Validate(sql, parameters);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains("where p.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("and upper(p.status::text) = upper(@Status)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("offset @Offset limit @Limit", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeSqlBuilder_RejectsPredicateWithManualAnd()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new SafeSqlBuilder("select 1 where true").And("and false"));
        Assert.Contains("sem AND/OR", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
