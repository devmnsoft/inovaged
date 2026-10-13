using System.Text.Json;
using System.Threading.Channels;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Preview;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Preview;

public sealed class PreviewQueue : IPreviewJobQueue
{
    public const int MaxPendingJobs = 2000;
    public const int PressureWarningJobs = 256;
    public static readonly TimeSpan AbandonedLease = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan PendingExpiration = TimeSpan.FromDays(7);

    public sealed record PreviewJob(Guid TenantId, Guid DocumentId, Guid VersionId, string StoragePath, string FileName, DateTimeOffset RequestedAt);

    private readonly IDbConnectionFactory _db;
    private readonly ILogger<PreviewQueue> _logger;
    private readonly Channel<Guid> _wake = Channel.CreateBounded<Guid>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Channel<PreviewJob> _fallback = Channel.CreateBounded<PreviewJob>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });

    public PreviewQueue(IDbConnectionFactory db, ILogger<PreviewQueue> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnqueueAsync(Guid tenantId, Guid documentId, Guid versionId, string storagePath, string fileName, CancellationToken ct)
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var job = new PreviewJob(tenantId, documentId, versionId, storagePath, fileName, requestedAt);
        var payload = JsonSerializer.Serialize(new
        {
            storagePath,
            fileName,
            documentId,
            requestedAt
        });

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var depth = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
SELECT count(*)::int
FROM ged.processing_job
WHERE reg_status = 'A'
  AND job_type = 'PREVIEW'
  AND status IN ('PENDING', 'PROCESSING');
""", cancellationToken: ct));
            if (depth >= MaxPendingJobs)
                throw new InvalidOperationException($"A fila durável de preview atingiu o limite de {MaxPendingJobs} jobs. A solicitação foi recusada para proteger o processamento.");
            if (depth >= PressureWarningJobs)
                _logger.LogWarning("Fila durável de preview sob pressão. Pendentes={Depth} Limite={Limit}", depth, MaxPendingJobs);

            await conn.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.processing_job (tenant_id, document_id, document_version_id, job_type, status, priority, payload)
VALUES (@tenantId, @documentId, @versionId, 'PREVIEW', 'PENDING', 3, @payload::jsonb)
ON CONFLICT DO NOTHING;
""", new { tenantId, documentId, versionId, payload }, cancellationToken: ct));
            _wake.Writer.TryWrite(versionId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            _logger.LogError(ex, "ged.processing_job.payload ausente. O job de preview será gravado sem caminho e o worker consultará a versão. Aplique 2026_10_24_preview_attempt_and_recovery.sql.");
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition("""
INSERT INTO ged.processing_job (tenant_id, document_id, document_version_id, job_type, status, priority)
VALUES (@tenantId, @documentId, @versionId, 'PREVIEW', 'PENDING', 3)
ON CONFLICT DO NOTHING;
""", new { tenantId, documentId, versionId }, cancellationToken: ct));
            _wake.Writer.TryWrite(versionId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogError(ex, "ged.processing_job ausente. O preview ficou apenas na fila limitada em memória e não sobrevive a reinício.");
            await _fallback.Writer.WriteAsync(job, ct);
        }
    }

    public bool TryTakeFallback(out PreviewJob job) => _fallback.Reader.TryRead(out job!);

    public async Task WaitForSignalAsync(CancellationToken ct)
    {
        await _wake.Reader.WaitToReadAsync(ct);
        while (_wake.Reader.TryRead(out _))
        {
        }
    }
}
