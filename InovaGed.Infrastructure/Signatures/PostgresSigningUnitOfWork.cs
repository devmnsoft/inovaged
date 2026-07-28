using System.Data;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Signatures;

namespace InovaGed.Infrastructure.Signatures;

public sealed class PostgresSigningUnitOfWork : ISigningUnitOfWork
{
    private readonly IDbConnection _connection;
    private readonly IDbTransaction _transaction;
    private bool _completed;

    public PostgresSigningUnitOfWork(IDbConnection connection, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        _connection = connection;
        _transaction = transaction;
        CancellationToken = cancellationToken;
    }

    public IDbConnection Connection => _connection;
    public IDbTransaction Transaction => _transaction;
    public CancellationToken CancellationToken { get; }

    public Task CommitAsync()
    {
        _transaction.Commit();
        _completed = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync()
    {
        if (!_completed) _transaction.Rollback();
        _completed = true;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed) _transaction.Rollback();
        _transaction.Dispose();
        if (_connection is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
        else _connection.Dispose();
    }
}

public sealed class PostgresSigningUnitOfWorkFactory(IDbConnectionFactory db) : ISigningUnitOfWorkFactory
{
    public async Task<ISigningUnitOfWork> BeginAsync(CancellationToken ct)
    {
        var connection = await db.OpenAsync(ct);
        var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        return new PostgresSigningUnitOfWork(connection, transaction, ct);
    }
}
