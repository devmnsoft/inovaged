using System.Collections.Concurrent;

namespace InovaGed.Infrastructure.Preview;

internal static class PreviewLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public static async Task<IDisposable> AcquireAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        var key = $"{tenantId:N}:{versionId:N}";
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(key, sem);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly string _key;
        private readonly SemaphoreSlim _sem;
        private bool _disposed;

        public Releaser(string key, SemaphoreSlim sem)
        {
            _key = key;
            _sem = sem;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sem.Release();

            // limpeza best-effort (não obrigatório)
            if (_sem.CurrentCount == 1)
            {
                // deixa no dict; remover pode dar race.
                // se quiser remover, faça com cuidado — eu recomendo manter.
            }
        }
    }
}
