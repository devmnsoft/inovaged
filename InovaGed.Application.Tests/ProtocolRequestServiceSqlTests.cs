using InovaGed.Application.Ged.Protocols;
using InovaGed.Infrastructure.Ged.Protocols;

namespace InovaGed.Application.Tests;

public sealed class ProtocolRequestServiceSqlTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static TheoryData<ProtocolWorkQueueFilter, ProtocolVisibilityScope> ListMyScenarios =>
        new()
        {
            { new ProtocolWorkQueueFilter(), new ProtocolVisibilityScope() },
            { new ProtocolWorkQueueFilter { Status = "REQUESTED" }, new ProtocolVisibilityScope() },
            { new ProtocolWorkQueueFilter { Search = "teste" }, new ProtocolVisibilityScope() },
            { new ProtocolWorkQueueFilter { From = FixedNow.AddDays(-7), To = FixedNow }, new ProtocolVisibilityScope() },
            { new ProtocolWorkQueueFilter { ShowAll = true }, new ProtocolVisibilityScope { IsAdmin = true } },
            { new ProtocolWorkQueueFilter { ShowAll = true }, new ProtocolVisibilityScope { IsAdministradorOphir = true } },
            { new ProtocolWorkQueueFilter { Priority = "HIGH" }, new ProtocolVisibilityScope() },
            { new ProtocolWorkQueueFilter { OnlyMine = true }, new ProtocolVisibilityScope() },
            { new ProtocolWorkQueueFilter { Overdue = true }, new ProtocolVisibilityScope() },
            { new ProtocolWorkQueueFilter { ReturnedForAdjustment = true }, new ProtocolVisibilityScope() }
        };

    public static TheoryData<ProtocolWorkQueueFilter, ProtocolVisibilityScope> WorkQueueScenarios =>
        new()
        {
            { new ProtocolWorkQueueFilter(), new ProtocolVisibilityScope { IsAdmin = true } },
            {
                new ProtocolWorkQueueFilter
                {
                    Status = "REQUESTED",
                    Search = "teste",
                    Priority = "HIGH",
                    From = FixedNow.AddDays(-7),
                    To = FixedNow,
                    OnlyMine = true,
                    Overdue = true,
                    ReturnedForAdjustment = true
                },
                new ProtocolVisibilityScope
                {
                    IsAdministradorOphir = true,
                    SectorId = Guid.Parse("22222222-2222-2222-2222-222222222222")
                }
            }
        };

    [Theory]
    [MemberData(nameof(ListMyScenarios))]
    public void BuildListMySql_KeepsProtocolRequestAliasAndWhereSeparated(ProtocolWorkQueueFilter filter, ProtocolVisibilityScope scope)
    {
        var sql = ProtocolRequestService.BuildListMySql(UserId, scope, filter, out _);

        Assert.Contains("from ged.protocol_request p" + Environment.NewLine + "where p.tenant_id = @TenantId", sql);
        Assert.DoesNotContain("pwhere", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("where and", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("and and", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(WorkQueueScenarios))]
    public void BuildListWorkQueueSql_KeepsProtocolRequestAliasAndWhereSeparated(ProtocolWorkQueueFilter filter, ProtocolVisibilityScope scope)
    {
        var sql = ProtocolRequestService.BuildListWorkQueueSql(UserId, scope, filter, out _);

        Assert.Contains("from ged.protocol_request p" + Environment.NewLine + "where p.tenant_id = @TenantId", sql);
        Assert.DoesNotContain("pwhere", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("where and", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("and and", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildListMySql_DoesNotAddUserScopeWhenAdminUsesShowAll()
    {
        var sql = ProtocolRequestService.BuildListMySql(UserId, new ProtocolVisibilityScope { IsAdmin = true }, new ProtocolWorkQueueFilter { ShowAll = true }, out _);

        Assert.DoesNotContain("p.requester_user_id = @UserId", sql);
        Assert.DoesNotContain("p.assigned_user_id = @UserId", sql);
    }

    [Fact]
    public void BuildListMySql_DoesNotAddUserScopeWhenAdministradorUsesShowAll()
    {
        var sql = ProtocolRequestService.BuildListMySql(UserId, new ProtocolVisibilityScope { IsAdministradorOphir = true }, new ProtocolWorkQueueFilter { ShowAll = true }, out _);

        Assert.DoesNotContain("p.requester_user_id = @UserId", sql);
        Assert.DoesNotContain("p.assigned_user_id = @UserId", sql);
    }
}
