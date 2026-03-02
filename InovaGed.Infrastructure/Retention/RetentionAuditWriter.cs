using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Retention;

public sealed class RetentionAuditWriter : IRetentionAuditWriter
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<RetentionAuditWriter> _logger;

    public RetentionAuditWriter(IDbConnectionFactory db, ILogger<RetentionAuditWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task WriteAsync(Guid tenantId, Guid actorId, object entityId, string eventType, string? message, CancellationToken ct)
    {
        // Placeholder: se você já tem tabela de audit geral, coloque aqui.
        _logger.LogInformation("AUDIT {Event} Tenant={Tenant} Actor={Actor} Entity={Entity} Msg={Msg}",
            eventType, tenantId, actorId, entityId, message);
        await Task.CompletedTask;
    }

    public async Task WriteDocAsync(Guid tenantId, Guid actorId, string? actorEmail, Guid documentId, string eventType, object data, CancellationToken ct)
    {
        const string sql = @"
insert into ged.document_audit(tenant_id, document_id, event_type, event_at, actor_id, actor_email, data)
values (@tenantId, @documentId, @eventType, now(), @actorId, @actorEmail, to_jsonb(@data::json));
";

        // Truque: Postgres não aceita @data::json direto do Dapper como object.
        // Vamos serializar em texto e converter via ::jsonb.
        var json = System.Text.Json.JsonSerializer.Serialize(data);

        const string sql2 = @"
insert into ged.document_audit(tenant_id, document_id, event_type, event_at, actor_id, actor_email, data)
values (@tenantId, @documentId, @eventType, now(), @actorId, @actorEmail, @json::jsonb);
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(sql2, new { tenantId, documentId, eventType, actorId, actorEmail, json });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WriteDocAsync failed");
        }
    }
}