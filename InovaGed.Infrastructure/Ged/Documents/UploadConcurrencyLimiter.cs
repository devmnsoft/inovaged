using System.Collections.Concurrent;
using InovaGed.Application.Ged.Documents;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class UploadConcurrencyLimiter : IUploadConcurrencyLimiter
{
    private readonly DocumentUploadOptions _options;
    private int _globalActive;
    private readonly ConcurrentDictionary<string, int> _userActive = new();
    private readonly ConcurrentDictionary<string, int> _batchActive = new();
    private readonly object _gate = new();

    public UploadConcurrencyLimiter(IOptions<DocumentUploadOptions> options) => _options = options.Value;

    public Task<UploadConcurrencyLease?> AcquireAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var userKey = $"{tenantId:N}:{userId:N}";
        var batchKey = $"{tenantId:N}:{batchId:N}";
        lock (_gate)
        {
            var userCount = _userActive.GetValueOrDefault(userKey);
            var batchCount = _batchActive.GetValueOrDefault(batchKey);
            if (_globalActive >= Math.Max(1, _options.MaxConcurrentUploadsGlobal) ||
                userCount >= Math.Max(1, _options.MaxConcurrentUploadsPerUser) ||
                batchCount >= Math.Max(1, _options.MaxConcurrentUploadsPerBatch))
            {
                return Task.FromResult<UploadConcurrencyLease?>(null);
            }

            _globalActive++;
            _userActive[userKey] = userCount + 1;
            _batchActive[batchKey] = batchCount + 1;
        }

        return Task.FromResult<UploadConcurrencyLease?>(new UploadConcurrencyLease(() => Release(userKey, batchKey)));
    }

    private void Release(string userKey, string batchKey)
    {
        lock (_gate)
        {
            _globalActive = Math.Max(0, _globalActive - 1);
            Decrement(_userActive, userKey);
            Decrement(_batchActive, batchKey);
        }
    }

    private static void Decrement(ConcurrentDictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key, out var value)) return;
        if (value <= 1) map.TryRemove(key, out _);
        else map[key] = value - 1;
    }
}
