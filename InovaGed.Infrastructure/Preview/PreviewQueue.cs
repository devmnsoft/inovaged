using System.Threading.Channels;
using InovaGed.Application.Preview;
namespace InovaGed.Infrastructure.Preview;
public sealed class PreviewQueue : IPreviewJobQueue
{
    public sealed record PreviewJob(Guid TenantId, Guid DocumentId, Guid VersionId, string StoragePath, string FileName);
    private readonly Channel<PreviewJob> _channel = Channel.CreateUnbounded<PreviewJob>();
    public Task EnqueueAsync(Guid tenantId, Guid documentId, Guid versionId, string storagePath, string fileName, CancellationToken ct)
        => _channel.Writer.WriteAsync(new PreviewJob(tenantId, documentId, versionId, storagePath, fileName), ct).AsTask();
    public IAsyncEnumerable<PreviewJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
