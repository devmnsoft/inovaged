using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;
using Npgsql;

namespace InovaGed.Infrastructure.Jobs;

public sealed class PostgresJobExecutionLock : IJobExecutionLock
{
    private readonly IDbConnectionFactory _db;
    public PostgresJobExecutionLock(IDbConnectionFactory db) => _db = db;
    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid tenantId, string jobName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var conn = (NpgsqlConnection)await _db.OpenAsync(cancellationToken);
        var key = (long)HashCode.Combine(tenantId, jobName);
        var ok = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select pg_try_advisory_lock(@Key)", new { Key = key }, cancellationToken: cancellationToken));
        return ok ? new Releaser(conn, key) : null;
    }
    private sealed class Releaser : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conn; private readonly long _key;
        public Releaser(NpgsqlConnection conn, long key) { _conn = conn; _key = key; }
        public async ValueTask DisposeAsync() { try { await _conn.ExecuteAsync("select pg_advisory_unlock(@Key)", new { Key = _key }); } finally { await _conn.DisposeAsync(); } }
    }
}
