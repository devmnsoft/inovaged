using Xunit;
using Dapper;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Infrastructure.Ged.Protocols;
using InovaGed.Infrastructure.Sql;

namespace InovaGed.Application.Tests.Infrastructure.Sql;

public sealed class ProtocolRequestServiceSqlTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SectorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void BuildListMySql_NoFilters_IsValid()
        => AssertProtocolSqlIsValid(ProtocolRequestService.BuildListMySql(UserId, new ProtocolVisibilityScope(), new ProtocolWorkQueueFilter(), out var parameters), parameters);

    [Fact]
    public void BuildListMySql_WithStatus_IsValid()
        => AssertProtocolSqlIsValid(ProtocolRequestService.BuildListMySql(UserId, new ProtocolVisibilityScope(), new ProtocolWorkQueueFilter { Status = "REQUESTED" }, out var parameters), parameters, "@Status", "status::text");

    [Fact]
    public void BuildListMySql_WithSearch_IsValid()
        => AssertProtocolSqlIsValid(ProtocolRequestService.BuildListMySql(UserId, new ProtocolVisibilityScope(), new ProtocolWorkQueueFilter { Search = "paciente" }, out var parameters), parameters, "@Search", "ilike @Search");

    [Fact]
    public void BuildListMySql_WithDateRange_IsValid()
        => AssertProtocolSqlIsValid(ProtocolRequestService.BuildListMySql(UserId, new ProtocolVisibilityScope(), new ProtocolWorkQueueFilter { From = DateTimeOffset.UtcNow.AddDays(-7), To = DateTimeOffset.UtcNow }, out var parameters), parameters, "@From", "@To");

    [Fact]
    public void BuildWorkQueueSql_Admin_IsValid()
        => AssertProtocolSqlIsValid(ProtocolRequestService.BuildListWorkQueueSql(UserId, new ProtocolVisibilityScope { IsAdmin = true }, new ProtocolWorkQueueFilter(), out var parameters), parameters);

    [Fact]
    public void BuildWorkQueueSql_Manager_IsValid()
        => AssertProtocolSqlIsValid(ProtocolRequestService.BuildListWorkQueueSql(UserId, new ProtocolVisibilityScope { IsAdministradorOphir = true, SectorId = SectorId }, new ProtocolWorkQueueFilter { OnlyMine = true }, out var parameters), parameters, "@SectorId");

    [Fact]
    public void BuildWorkQueueSql_Archivist_IsValid()
        => AssertProtocolSqlIsValid(ProtocolRequestService.BuildListWorkQueueSql(UserId, new ProtocolVisibilityScope { IsArquivistaOphir = true }, new ProtocolWorkQueueFilter { Overdue = true, ReturnedForAdjustment = true }, out var parameters), parameters, "RETURNED_FOR_ADJUSTMENT", "due_at < now()");

    private static void AssertProtocolSqlIsValid(string sql, DynamicParameters parameters, params string[] expectedFragments)
    {
        parameters.Add("TenantId", TenantId);
        var result = SqlSafetyValidator.Validate(sql, parameters);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains("from ged.protocol_request p" + Environment.NewLine + "where p.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("order by p.requested_at desc", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("offset @Offset limit @Limit", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pwhere", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("where and", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("and and", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var fragment in expectedFragments) Assert.Contains(fragment, sql, StringComparison.OrdinalIgnoreCase);
    }
}
