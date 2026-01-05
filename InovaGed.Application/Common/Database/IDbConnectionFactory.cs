using Npgsql;

namespace InovaGed.Application.Common.Database;

public interface IDbConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct);
}
