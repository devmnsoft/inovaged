using System.Data;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SmartSearch;
using InovaGed.Infrastructure.SmartSearch;
using Npgsql;

namespace InovaGed.Application.Tests;

public sealed class SmartSearchEvolutionTests
{
    [Fact]
    public async Task Parser_PreservesProtocolFormattingAndLeadingZeros()
    {
        var intent = await CreateParser().ParseAsync(Guid.NewGuid(), "protocolo 001234/2026",
            new SmartSearchRequest { Query = "protocolo 001234/2026" }, CancellationToken.None);

        Assert.Equal("001234/2026", intent.ProtocolNumber);
        Assert.Equal(SmartSearchIntentKind.ByProtocol, intent.Kind);
    }

    [Fact]
    public async Task Parser_ExtractsQuotedPhraseWithoutExecutingIt()
    {
        var intent = await CreateParser().ParseAsync(Guid.NewGuid(), "contrato \"manutenção preventiva\" 2025",
            new SmartSearchRequest { Query = "contrato \"manutenção preventiva\" 2025" }, CancellationToken.None);

        Assert.Equal(["manutenção preventiva"], intent.ExactPhrases);
        Assert.Equal(2025, intent.Year);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), intent.From);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), intent.To);
    }

    [Fact]
    public void SavedSearchReadModel_IsSafeForDapperMaterialization()
    {
        var type = typeof(SmartSearchSavedSearch);
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        foreach (var property in type.GetProperties()) Assert.True(property.SetMethod?.IsPublic, property.Name);
        Assert.Equal(typeof(DateTime?), type.GetProperty(nameof(SmartSearchSavedSearch.UpdatedAt))!.PropertyType);
        Assert.Equal(typeof(DateTime?), type.GetProperty(nameof(SmartSearchSavedSearch.LastRunAt))!.PropertyType);
        Assert.Equal(typeof(int), type.GetProperty(nameof(SmartSearchSavedSearch.RunCount))!.PropertyType);
        Assert.Equal(typeof(bool), type.GetProperty(nameof(SmartSearchSavedSearch.IsFavorite))!.PropertyType);
    }

    private static SmartQueryParser CreateParser() => new(new UnusedDb(), new EmptyContextParser());

    private sealed class EmptyContextParser : ISmartSearchContextParser
    {
        public Task<SmartSearchContextIntent> ParseAsync(Guid tenantId, string query, CancellationToken ct) =>
            Task.FromResult(new SmartSearchContextIntent { OriginalQuery = query, NormalizedQuery = query });
    }

    private sealed class UnusedDb : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => throw new NotSupportedException();
        public Task<NpgsqlConnection> OpenAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
