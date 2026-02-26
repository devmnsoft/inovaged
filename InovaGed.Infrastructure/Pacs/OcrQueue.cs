using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Pacs;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Pacs;

public sealed class OcrQueue : IOcrQueue
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OcrQueue> _logger;

    public OcrQueue(IDbConnectionFactory db, ILogger<OcrQueue> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnqueuePacsAsync(Guid tenantId, Guid ticketId, Guid ticketFileId, string storageRelPath, CancellationToken ct)
    {
        try
        {
            const string sql = @"
insert into pacs.ocr_job (tenant_id, ticket_id, ticket_file_id, storage_rel_path, status, created_at)
values (@tenantId, @ticketId, @ticketFileId, @storageRelPath, 'PENDING', now());
";

            using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenantId,
                ticketId,
                ticketFileId,
                storageRelPath
            }, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "update pacs.ticket set status='OCR_QUEUED' where tenant_id=@tenantId and id=@ticketId",
                new { tenantId, ticketId }, cancellationToken: ct));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PACS: enqueue OCR cancelado. Tenant={Tenant} Ticket={Ticket}", tenantId, ticketId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PACS: erro ao enfileirar OCR. Tenant={Tenant} Ticket={Ticket} File={File}", tenantId, ticketId, ticketFileId);
            throw;
        }
    }
}