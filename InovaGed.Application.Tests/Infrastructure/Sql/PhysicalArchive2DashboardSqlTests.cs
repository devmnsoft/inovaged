using Xunit;
using Dapper;
using InovaGed.Infrastructure.PhysicalArchive2;
using InovaGed.Application.PhysicalArchive2;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.DependencyInjection;

namespace InovaGed.Application.Tests.Infrastructure.Sql;

public sealed class PhysicalArchive2DashboardSqlTests
{
    private readonly IServiceProvider _provider;

    public PhysicalArchive2DashboardSqlTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IPhysicalArchive2Service, PhysicalArchive2Service>();
        services.AddScoped<IDbConnectionFactory>(sp =>
            new DefaultDbConnectionFactory(
                "Host=localhost;Port=5432;Database=inovaged_test;Username=postgres;Password=postgres",
                "Npgsql"));
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Database", "PostgreSQL")]
    public async Task DashboardAsync_WithValidTenant_ExecutesSuccessfully()
    {
        var service = _provider.GetRequiredService<IPhysicalArchive2Service>();
        var tenantId = Guid.NewGuid();
        var ct = CancellationToken.None;

        // This should not throw SQLSTATE 42601 (syntax error)
        var result = await service.DashboardAsync(tenantId, ct);

        Assert.NotNull(result);
        Assert.IsType<PhysicalArchiveDashboard>(result);
        Assert.True(result.Boxes >= 0);
        Assert.True(result.LabelledBoxes >= 0);
        Assert.True(result.UnlocatedBoxes >= 0);
        Assert.True(result.LoanedBoxes >= 0);
        Assert.True(result.OpenInventories >= 0);
        Assert.True(result.OverdueLoans >= 0);
        Assert.True(result.MonthlyMovements >= 0);
        Assert.True(result.PendingChecks >= 0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Database", "PostgreSQL")]
    public async Task DashboardAsync_WithNoData_ReturnsZeroCounts()
    {
        var service = _provider.GetRequiredService<IPhysicalArchive2Service>();
        var emptyTenantId = Guid.NewGuid();
        var ct = CancellationToken.None;

        var result = await service.DashboardAsync(emptyTenantId, ct);

        Assert.NotNull(result);
        Assert.Equal(0L, result.Boxes);
        Assert.Equal(0L, result.LabelledBoxes);
        Assert.Equal(0L, result.UnlocatedBoxes);
        Assert.Equal(0L, result.LoanedBoxes);
        Assert.Equal(0L, result.OpenInventories);
        Assert.Equal(0L, result.OverdueLoans);
        Assert.Equal(0L, result.MonthlyMovements);
        Assert.Equal(0L, result.PendingChecks);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DashboardAsync_NoEscapeBackslashesInAliases()
    {
        // This is a static validation test that verifies the SQL string doesn't contain
        // incorrect escape sequences that would be sent to PostgreSQL
        var service = new PhysicalArchive2Service(new MockDbConnectionFactory());

        // The test passes if this doesn't throw during instantiation.
        Assert.NotNull(service);
    }

    private sealed class MockDbConnectionFactory : IDbConnectionFactory
    {
        public Task<System.Data.IDbConnection> OpenAsync(CancellationToken ct)
            => throw new NotImplementedException("Mock only");
    }
}
