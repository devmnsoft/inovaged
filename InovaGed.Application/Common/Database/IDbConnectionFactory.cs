using System.Data;
using Npgsql;

namespace InovaGed.Application.Common.Database;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct);
}
