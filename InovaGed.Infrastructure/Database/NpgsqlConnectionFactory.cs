using Npgsql;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Common.Database;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _cs;

    public NpgsqlConnectionFactory(string connectionString)
        => _cs = connectionString;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        return conn; // ✅ quem chama deve usar await using
    }
}
