using System.Data;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SmartSearch;
using InovaGed.Infrastructure.SmartSearch;
using Npgsql;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class SmartQueryParserDateTests
{
    [Theory]
    [InlineData("APAC 2022", 2022, 1, 1, 2023, 1, 1)]
    [InlineData("documento 05/2026", 2026, 5, 1, 2026, 6, 1)]
    [InlineData("meados de 2022", 2022, 5, 1, 2022, 9, 1)]
    public async Task ParseAsync_CreatesUtcExclusiveDateRanges(string query, int fromYear, int fromMonth, int fromDay, int toYear, int toMonth, int toDay)
    {
        var parser = new SmartQueryParser(new ThrowingDbConnectionFactory());

        var intent = await parser.ParseAsync(Guid.NewGuid(), query, new SmartSearchRequest { Query = query }, CancellationToken.None);

        Assert.NotNull(intent.From);
        Assert.NotNull(intent.To);
        Assert.Equal(DateTimeKind.Utc, intent.From.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, intent.To.Value.Kind);
        Assert.Equal(new DateTime(fromYear, fromMonth, fromDay, 0, 0, 0, DateTimeKind.Utc), intent.From.Value);
        Assert.Equal(new DateTime(toYear, toMonth, toDay, 0, 0, 0, DateTimeKind.Utc), intent.To.Value);
    }

    [Fact]
    public async Task ParseAsync_NormalizesRequestDatesToUtc()
    {
        var parser = new SmartQueryParser(new ThrowingDbConnectionFactory());
        var request = new SmartSearchRequest
        {
            Query = "APAC",
            From = new DateTime(2026, 5, 1),
            To = new DateTime(2026, 6, 1)
        };

        var intent = await parser.ParseAsync(Guid.NewGuid(), request.Query, request, CancellationToken.None);

        Assert.Equal(DateTimeKind.Utc, intent.From!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, intent.To!.Value.Kind);
    }

    private sealed class ThrowingDbConnectionFactory : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() =>
            throw new InvalidOperationException("O teste não deve abrir conexão síncrona.");
        public Task<NpgsqlConnection> OpenAsync(CancellationToken ct) =>
            Task.FromException<NpgsqlConnection>(new InvalidOperationException("O teste não deve abrir conexão assíncrona."));
    }
}
