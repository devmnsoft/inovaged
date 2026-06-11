using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Protocols;

public sealed class ProtocolHistoryWriter : IProtocolHistoryWriter
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ProtocolHistoryWriter> _logger;
    public ProtocolHistoryWriter(IDbConnectionFactory db, ILogger<ProtocolHistoryWriter> logger) { _db = db; _logger = logger; }

    public async Task WriteAsync(Guid tenantId, Guid protocolRequestId, string action, string? oldStatus, string? newStatus, Guid? userId, string? userName, Guid? sectorId, string? sectorName, string? reason, string? internalNotes, object? metadata, string? correlationId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.protocol_request_history
(id, tenant_id, protocol_request_id, old_status, new_status, action, user_id, user_name, sector_id, sector_name, reason, internal_notes, metadata_json, correlation_id, created_at, reg_status)
values (gen_random_uuid(), @TenantId, @ProtocolRequestId, @OldStatus, @NewStatus, @Action, @UserId, @UserName, @SectorId, @SectorName, @Reason, @InternalNotes, cast(@MetadataJson as jsonb), @CorrelationId, now(), 'A');
""", new { TenantId = tenantId, ProtocolRequestId = protocolRequestId, OldStatus = oldStatus, NewStatus = newStatus, Action = action, UserId = userId, UserName = userName, SectorId = sectorId, SectorName = sectorName, Reason = reason, InternalNotes = internalNotes, MetadataJson = JsonSerializer.Serialize(metadata ?? new { }), CorrelationId = correlationId ?? Guid.NewGuid().ToString("N") }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gravar histórico de protocolo. Tenant={Tenant} Protocol={Protocol} Action={Action}", tenantId, protocolRequestId, action);
        }
    }
}
