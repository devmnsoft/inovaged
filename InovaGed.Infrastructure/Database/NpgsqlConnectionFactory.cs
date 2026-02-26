using System.Data;
using InovaGed.Application.Common.Database;
using Npgsql;

namespace InovaGed.Infrastructure.Common.Database;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _cs;

    public NpgsqlConnectionFactory(string connectionString)
    {
        _cs = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("ConnectionString inválida.", nameof(connectionString))
            : connectionString;
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_cs);
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    public IDbConnection Open()
    {
        var conn = new NpgsqlConnection(_cs);
        conn.Open();
        return conn;
    }
}