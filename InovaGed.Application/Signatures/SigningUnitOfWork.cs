using System.Data;

namespace InovaGed.Application.Signatures;

public interface ISigningUnitOfWork : IAsyncDisposable
{
    IDbConnection Connection { get; }
    IDbTransaction Transaction { get; }
    CancellationToken CancellationToken { get; }
    Task CommitAsync();
    Task RollbackAsync();
}

public interface ISigningUnitOfWorkFactory
{
    Task<ISigningUnitOfWork> BeginAsync(CancellationToken ct);
}

public sealed record SigningTransactionContext(IDbConnection Connection, IDbTransaction Transaction, CancellationToken CancellationToken);
