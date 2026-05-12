using System.Data;
using InovaGed.Application.Common.Database;
using Npgsql;

namespace InovaGed.Infrastructure.Common.Database;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _cs;
    private static readonly TimeSpan[] OpenRetryBackoff =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(700),
        TimeSpan.FromMilliseconds(1500)
    };

    public NpgsqlConnectionFactory(string connectionString)
    {
        _cs = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("ConnectionString inválida.", nameof(connectionString))
            : connectionString;
    }

    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(_cs);
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var conn = new NpgsqlConnection(_cs);

            try
            {
                await conn.OpenAsync(ct);
                return conn;
            }
            catch (PostgresException ex) when (
                (ex.SqlState == "53300" || ex.SqlState == "57P03") &&
                attempt < OpenRetryBackoff.Length)
            {
                await conn.DisposeAsync();
                await Task.Delay(OpenRetryBackoff[attempt], ct);
            }
            catch
            {
                await conn.DisposeAsync();
                throw;
            }
        }
    }
}
