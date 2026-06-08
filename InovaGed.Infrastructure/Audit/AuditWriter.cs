using System.Text.Json;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Audit;

public sealed class AuditWriter : IAuditWriter
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<AuditWriter> _logger;

    public AuditWriter(IDbConnectionFactory db, ILogger<AuditWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result> WriteAsync(
        Guid tenantId,
        Guid? userId,
        string action,
        string entityName,
        Guid? entityId,
        string? summary,
        string? ipAddress,
        string? userAgent,
        object? data,
        CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (string.IsNullOrWhiteSpace(action)) return Result.Fail("ACTION", "Action inválida.");
            action = MapAction(action);
            if (string.IsNullOrWhiteSpace(entityName)) return Result.Fail("ENTITY", "EntityName inválido.");

            using var conn = await _db.OpenAsync(ct);

            var json = data is null ? null : JsonSerializer.Serialize(data);

            const string sql = @"
insert into ged.audit_log
(tenant_id, user_id, action, entity_name, entity_id, summary, ip_address, user_agent, data)
values
(@tenant_id, @user_id, @action::ged.audit_action_enum, @entity_name, @entity_id, @summary, @ip_address, @user_agent, @data::jsonb);
";

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                user_id = userId,
                action,
                entity_name = entityName,
                entity_id = entityId,
                summary,
                ip_address = ipAddress,
                user_agent = userAgent,
                data = json
            }, cancellationToken: ct));

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AuditWriter.WriteAsync failed. Tenant={Tenant} Action={Action} Entity={Entity} Id={Id}",
                tenantId, action, entityName, entityId);

            // auditoria não pode derrubar o fluxo principal
            return Result.Fail("AUDIT", "Falha ao registrar auditoria.");
        }
    }

    private static string MapAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return "VIEW";

        var normalized = action.Trim().ToUpperInvariant();

        return normalized switch
        {
            "HTTP" => "HTTP",
            "GET" => "HTTP",
            "POST" => "HTTP",
            "PUT" => "HTTP",
            "PATCH" => "HTTP",
            "DELETE" => "HTTP",

            "CREATE" => "CREATE",
            "INSERT" => "CREATE",
            "ADD" => "CREATE",

            "UPDATE" => "UPDATE",
            "EDIT" => "UPDATE",
            "ALTER" => "UPDATE",

            "DELETE_LOGICAL" => "DELETE",
            "REMOVE" => "DELETE",

            "VIEW" => "VIEW",
            "READ" => "VIEW",
            "DETAIL" => "VIEW",

            "DOWNLOAD" => "DOWNLOAD",
            "UPLOAD" => "UPLOAD",
            "LOGIN" => "LOGIN",
            "LOGOUT" => "LOGOUT",
            "EXPORT" => "EXPORT",
            "IMPORT" => "IMPORT",
            "DOCUMENT_MARK_INCOMPLETE" => "DOCUMENT_MARK_INCOMPLETE",
            "DOCUMENT_PART_MARK_INCOMPLETE" => "DOCUMENT_MARK_INCOMPLETE",
            "DOCUMENT_PART_ADD" => "DOCUMENT_PART_ADD",
            "UPLOAD_DOCUMENT_PART" => "DOCUMENT_PART_ADD",
            "DOCUMENT_PART_CREATE" => "DOCUMENT_PART_ADD",
            "DOCUMENT_PART_UPLOAD" => "DOCUMENT_PART_ADD",
            "DOCUMENT_PANEL_VIEW" => "DOCUMENT_PANEL_VIEW",
            "FILE_PREVIEW" => "FILE_PREVIEW",
            "OCR_VIEW" => "OCR_VIEW",
            "DOCUMENT_HISTORY_VIEW" => "DOCUMENT_HISTORY_VIEW",
            "DOCUMENT_PART_VIEW" => "DOCUMENT_PART_VIEW",
            "DOCUMENT_PART_CONSOLIDATE" => "DOCUMENT_PART_CONSOLIDATE",
            "DOCUMENT_PART_CANCEL" => "DOCUMENT_PART_CANCEL",
            "DOCUMENT_PART_MARK_COMPLETE" => "DOCUMENT_PART_MARK_COMPLETE",
            "DOCUMENT_PART_COMPLETE" => "DOCUMENT_PART_MARK_COMPLETE",
            "DOCUMENT_PART_PREVIEW" => "DOCUMENT_PART_PREVIEW",
            "DOCUMENT_PART_DOWNLOAD" => "DOCUMENT_PART_DOWNLOAD",

            _ => "VIEW"
        };
    }
}