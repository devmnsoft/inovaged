using System.Data;

namespace InovaGed.Application.Common.Database;

public interface IDbConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken ct);
}
