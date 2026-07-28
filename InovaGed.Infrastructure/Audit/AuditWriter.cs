using System.Data;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Audit;

public sealed class AuditWriter : IAuditWriter
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<AuditWriter> _logger;
    private readonly IConfiguration _configuration;

    public AuditWriter(IDbConnectionFactory db, ILogger<AuditWriter> logger, IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
    }

    public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return WriteCoreAsync(command.TenantId, command.UserId, command.Action, command.EntityName,
            command.EntityId, command.Summary, command.IpAddress, command.UserAgent,
            MergeAuditMetadata(command), command.CorrelationId, command.EventType, ct);
    }

    [Obsolete("Use WriteAsync(AuditWriteCommand, CancellationToken) para evitar inversão de argumentos.")]
    public Task<Result> WriteAsync(
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
        => WriteCoreAsync(tenantId, userId, action, entityName, entityId, summary, ipAddress,
            userAgent, data, null, "INFO", ct);

    private async Task<Result> WriteCoreAsync(
        Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId,
        string? summary, string? ipAddress, string? userAgent, object? data,
        string? correlationId, string eventType, CancellationToken ct)
    {
        var strictAudit = _configuration.GetValue<bool>("AuditLogs:StrictAudit") ||
            (_configuration.GetValue<bool>("AuditLogs:StrictAuthenticationAudit") &&
             string.Equals(entityName, "authentication", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (!_configuration.GetValue("AuditLogs:Enabled", true)) return Result.Ok();
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (string.IsNullOrWhiteSpace(action)) return Result.Fail("ACTION", "Action inválida.");
            action = MapAction(action);
            if (string.IsNullOrWhiteSpace(entityName)) return Result.Fail("ENTITY", "EntityName inválido.");

            await using var conn = await _db.OpenAsync(ct);

            var json = data is null ? null : JsonSerializer.Serialize(data);
            var normalizedUserId = await NormalizeAuditUserIdAsync(conn, tenantId, userId, ct);
            var userName = ResolveUserName(userId, normalizedUserId);
            var preferAppAuditLog = _configuration.GetValue("AuditLogs:PreferAppAuditLog", true);

            if (preferAppAuditLog && await TableExistsAsync(conn, "app_audit_log", ct))
            {
                await WriteAppAuditLogAsync(conn, tenantId, normalizedUserId, userName, action, entityName, entityId, summary, ipAddress, userAgent, json, correlationId, eventType, ct);
                return Result.Ok();
            }

            if (preferAppAuditLog)
            {
                _logger.LogWarning("app_audit_log não existe. Execute migrations. Tenant={TenantId} Action={Action} Entity={EntityName}", tenantId, action, entityName);
            }

            if (await TableExistsAsync(conn, "audit_log", ct))
            {
                await WriteLegacyAuditLogAsync(conn, tenantId, normalizedUserId, action, entityName, entityId, summary, ipAddress, userAgent, json, ct);
                return Result.Ok();
            }

            _logger.LogWarning("Nenhuma tabela de auditoria encontrada. Execute migrations. Tenant={TenantId} Action={Action} Entity={EntityName}", tenantId, action, entityName);
            return Result.Ok();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503" && !strictAudit)
        {
            _logger.LogWarning(ex,
                "Auditoria ignorada por FK inválida. Tenant={TenantId} User={UserId} Action={Action} Entity={EntityName}",
                tenantId, userId, action, entityName);
            return Result.Ok();
        }
        catch (Exception ex) when (!strictAudit)
        {
            _logger.LogError(ex,
                "AuditWriter.WriteAsync failed. Tenant={Tenant} Action={Action} Entity={Entity} Id={Id}",
                tenantId, action, entityName, entityId);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AuditWriter.WriteAsync failed in StrictAudit mode. Tenant={Tenant} Action={Action} Entity={Entity} Id={Id}",
                tenantId, action, entityName, entityId);
            throw;
        }
    }

    private static async Task WriteAppAuditLogAsync(IDbConnection conn, Guid tenantId, Guid? userId, string userName, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, string? details, string? correlationId, string eventType, CancellationToken ct)
    {
        const string sql = @"
insert into ged.app_audit_log
(
    id,
    tenant_id,
    user_id,
    user_name,
    action,
    event_type,
    source,
    entity_name,
    entity_id,
    method,
    path,
    status_code,
    message,
    details,
    correlation_id,
    ip_address,
    user_agent,
    created_at,
    reg_status
)
values
(
    gen_random_uuid(),
    @TenantId,
    @UserId,
    @UserName,
    @Action,
    @EventType,
    @Source,
    @EntityName,
    @EntityId,
    @Method,
    @Path,
    @StatusCode,
    @Message,
    @Details::jsonb,
    @CorrelationId,
    @IpAddress,
    @UserAgent,
    now(),
    'A'
);";

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            TenantId = tenantId,
            UserId = userId,
            UserName = userName,
            Action = action,
            EventType = NormalizeEventType(eventType),
            Source = "AuditWriter",
            EntityName = entityName,
            EntityId = entityId?.ToString(),
            Method = (string?)null,
            Path = (string?)null,
            StatusCode = (int?)null,
            Message = summary,
            Details = details,
            CorrelationId = correlationId,
            IpAddress = ipAddress,
            UserAgent = userAgent
        }, cancellationToken: ct));
    }

    private static async Task WriteLegacyAuditLogAsync(IDbConnection conn, Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, string? data, CancellationToken ct)
    {
        const string sql = @"
insert into ged.audit_log
(tenant_id, user_id, action, entity_name, entity_id, summary, ip_address, user_agent, data)
values
(@tenant_id, @user_id, @action::ged.audit_action_enum, @entity_name, @entity_id, @summary, @ip_address, @user_agent, @data::jsonb);";

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
            data
        }, cancellationToken: ct));
    }

    private static async Task<Guid?> NormalizeAuditUserIdAsync(IDbConnection conn, Guid tenantId, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty)
            return null;

        var appUserExists = await TableExistsAsync(conn, "app_user", ct);
        if (!appUserExists)
            return null;

        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
select exists (
    select 1
    from ged.app_user
    where tenant_id = @TenantId
      and id = @UserId
);", new { TenantId = tenantId, UserId = userId.Value }, cancellationToken: ct));

        return exists ? userId.Value : null;
    }

    private static async Task<bool> TableExistsAsync(IDbConnection conn, string tableName, CancellationToken ct)
        => await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
select exists (
    select 1
    from information_schema.tables
    where table_schema = 'ged'
      and table_name = @TableName
);", new { TableName = tableName }, cancellationToken: ct));

    private static string ResolveUserName(Guid? originalUserId, Guid? normalizedUserId)
    {
        if (normalizedUserId.HasValue)
            return normalizedUserId.Value.ToString();

        if (originalUserId.HasValue && originalUserId.Value != Guid.Empty)
            return originalUserId.Value.ToString();

        return "Sistema";
    }

    private static object? MergeAuditMetadata(AuditWriteCommand command)
    {
        if (command.Data is null && command.CorrelationId is null && command.Outcome is null && command.ReasonCode is null)
            return null;

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (command.Data is not null)
        {
            var element = JsonSerializer.SerializeToElement(command.Data);
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                    values[property.Name] = property.Value.Clone();
            else
                values["data"] = element;
        }
        if (!string.IsNullOrWhiteSpace(command.CorrelationId)) values["correlationId"] = command.CorrelationId;
        if (!string.IsNullOrWhiteSpace(command.Outcome)) values["outcome"] = command.Outcome;
        if (!string.IsNullOrWhiteSpace(command.ReasonCode)) values["reasonCode"] = command.ReasonCode;
        return values;
    }

    private static string NormalizeEventType(string? eventType)
        => eventType?.Trim().ToUpperInvariant() switch
        {
            "SECURITY" => "SECURITY",
            "ERROR" => "ERROR",
            _ => "INFO"
        };

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
            "CODE_GENERATED" => "CODE_GENERATED",
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
            "DOCUMENT_CLASSIFICATION_CHANGED" => "DOCUMENT_CLASSIFICATION_CHANGED",
            "PROTOCOLO_REQUEST_CREATED" => "PROTOCOLO_REQUEST_CREATED",
            "PROTOCOLO_REQUEST_UPDATED" => "PROTOCOLO_REQUEST_UPDATED",
            "PROTOCOLO_ASSIGNED" => "PROTOCOLO_ASSIGNED",
            "PROTOCOLO_APPROVED" => "PROTOCOLO_APPROVED",
            "PROTOCOLO_REJECTED" => "PROTOCOLO_REJECTED",
            "PROTOCOLO_MOVED" => "PROTOCOLO_MOVED",
            "PROTOCOLO_FINISHED" => "PROTOCOLO_FINISHED",
            "LOAN_REQUEST_CREATED" => "LOAN_REQUEST_CREATED",
            "LOAN_APPROVED" => "LOAN_APPROVED",
            "LOAN_REJECTED" => "LOAN_REJECTED",
            "LOAN_DELIVERED" => "LOAN_DELIVERED",
            "LOAN_RETURNED" => "LOAN_RETURNED",
            "LOAN_OVERDUE" => "LOAN_OVERDUE",
            "GED_FOLDER_MOVED" => "GED_FOLDER_MOVED",

            _ => "VIEW"
        };
    }
}
