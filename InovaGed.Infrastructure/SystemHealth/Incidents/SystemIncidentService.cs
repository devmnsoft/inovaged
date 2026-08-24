using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth.Incidents;

namespace InovaGed.Infrastructure.SystemHealth.Incidents;

public sealed class SystemIncidentService : ISystemIncidentService, IRouteHealthRecorder
{
    private readonly IDbConnectionFactory _db;

    public SystemIncidentService(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<Guid> RegisterAsync(SystemIncidentCreateCommand command, CancellationToken ct)
    {
        var signature = Hash(string.Join(
            '|',
            command.IncidentType,
            command.ExceptionType,
            command.SqlState,
            command.Controller,
            command.Action,
            command.Path,
            command.DatabaseObject,
            Normalize(command.Message)));

        await using var connection = await _db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var existingId = await connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                """
                select id
                from ged.system_incident
                where signature_hash = @Signature
                  and status in ('OPEN','IN_REVIEW')
                  and (@TenantId is null or tenant_id = @TenantId)
                order by last_seen_at desc
                limit 1
                for update
                """,
                new
                {
                    Signature = signature,
                    command.TenantId
                },
                tx,
                cancellationToken: ct));

        var grouped = existingId.HasValue;
        Guid incidentId;

        if (grouped)
        {
            incidentId = existingId.Value;
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    update ged.system_incident
                    set occurrence_count = occurrence_count + 1,
                        last_seen_at = now(),
                        correlation_id = coalesce(@CorrelationId, correlation_id)
                    where id = @IncidentId
                    """,
                    new
                    {
                        IncidentId = incidentId,
                        command.CorrelationId
                    },
                    tx,
                    cancellationToken: ct));
        }
        else
        {
            incidentId = await connection.ExecuteScalarAsync<Guid>(
                new CommandDefinition(
                    """
                    insert into ged.system_incident(
                        tenant_id,
                        correlation_id,
                        incident_type,
                        severity,
                        signature_hash,
                        title,
                        message,
                        technical_message,
                        recommended_action,
                        recommended_script,
                        controller,
                        action,
                        path,
                        http_method,
                        sql_state,
                        database_object,
                        exception_type,
                        stack_trace,
                        payload_json
                    )
                    values(
                        @TenantId,
                        @CorrelationId,
                        @IncidentType,
                        @Severity,
                        @Signature,
                        @Title,
                        @Message,
                        @TechnicalMessage,
                        @RecommendedAction,
                        @RecommendedScript,
                        @Controller,
                        @Action,
                        @Path,
                        @HttpMethod,
                        @SqlState,
                        @DatabaseObject,
                        @ExceptionType,
                        @StackTrace,
                        cast(@PayloadJson as jsonb)
                    )
                    returning id
                    """,
                    new
                    {
                        command.TenantId,
                        command.CorrelationId,
                        command.IncidentType,
                        command.Severity,
                        Signature = signature,
                        command.Title,
                        command.Message,
                        command.TechnicalMessage,
                        command.RecommendedAction,
                        command.RecommendedScript,
                        command.Controller,
                        command.Action,
                        command.Path,
                        command.HttpMethod,
                        command.SqlState,
                        command.DatabaseObject,
                        command.ExceptionType,
                        command.StackTrace,
                        PayloadJson = Redact(command.PayloadJson)
                    },
                    tx,
                    cancellationToken: ct));
        }

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                insert into ged.system_incident_event(
                    incident_id,
                    tenant_id,
                    correlation_id,
                    event_type,
                    event_message
                )
                values(
                    @IncidentId,
                    @TenantId,
                    @CorrelationId,
                    @EventType,
                    @Message
                )
                """,
                new
                {
                    IncidentId = incidentId,
                    command.TenantId,
                    command.CorrelationId,
                    EventType = grouped ? "OCCURRENCE" : "CREATED",
                    Message = command.Title
                },
                tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);
        return incidentId;
    }

    public async Task<IReadOnlyList<SystemIncidentListItem>> ListAsync(
        SystemIncidentFilter filter,
        CancellationToken ct)
    {
        await using var connection = await _db.OpenAsync(ct);
        var rows = await connection.QueryAsync<SystemIncidentListDbRow>(
            new CommandDefinition(
                """
                select id, incident_type "IncidentType", severity, status, title, message,
                       recommended_action "RecommendedAction", controller, action, path,
                       correlation_id "CorrelationId", occurrence_count "OccurrenceCount",
                       first_seen_at "FirstSeenAt", last_seen_at "LastSeenAt"
                from ged.system_incident
                where reg_status = 'A'
                  and (@Type is null or incident_type = @Type)
                  and (@Severity is null or severity = @Severity)
                  and (@Status is null or status = @Status)
                  and (@Controller is null or controller ilike '%' || @Controller || '%')
                  and (@Action is null or action ilike '%' || @Action || '%')
                  and (@Path is null or path ilike '%' || @Path || '%')
                  and (@CorrelationId is null or correlation_id = @CorrelationId)
                  and (@From is null or last_seen_at >= @From)
                  and (@To is null or last_seen_at <= @To)
                  and (@TenantId is null or tenant_id = @TenantId)
                order by last_seen_at desc
                limit @Limit
                """,
                filter,
                cancellationToken: ct));

        return rows.Select(row => row.ToModel()).ToList();
    }

    public async Task<SystemIncidentDetails?> GetAsync(Guid incidentId, CancellationToken ct)
    {
        await using var connection = await _db.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<DetailDbRow>(
            new CommandDefinition(
                """
                select id, incident_type "IncidentType", severity, status, title, message,
                       technical_message "TechnicalMessage", recommended_action "RecommendedAction",
                       recommended_script "RecommendedScript", controller, action, path,
                       http_method "HttpMethod", correlation_id "CorrelationId", sql_state "SqlState",
                       database_object "DatabaseObject", exception_type "ExceptionType",
                       stack_trace "StackTrace", occurrence_count "OccurrenceCount",
                       first_seen_at "FirstSeenAt", last_seen_at "LastSeenAt",
                       resolved_by "ResolvedBy", resolved_at "ResolvedAt",
                       resolution_notes "ResolutionNotes"
                from ged.system_incident
                where id = @IncidentId and reg_status = 'A'
                """,
                new { IncidentId = incidentId },
                cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        var eventRows = await connection.QueryAsync<SystemIncidentEventDbRow>(
            new CommandDefinition(
                """
                select id, event_type "EventType", event_message "EventMessage",
                       occurred_at "OccurredAt", correlation_id "CorrelationId", user_name "UserName"
                from ged.system_incident_event
                where incident_id = @IncidentId
                order by occurred_at desc
                """,
                new { IncidentId = incidentId },
                cancellationToken: ct));
        var events = eventRows.Select(eventRow => eventRow.ToModel()).ToList();

        return row.ToModel(events);
    }

    public Task ResolveAsync(Guid incidentId, Guid userId, string notes, CancellationToken ct) =>
        SetStatus(incidentId, userId, IncidentStatus.Resolved, notes, ct);

    public Task IgnoreAsync(Guid incidentId, Guid userId, string reason, CancellationToken ct) =>
        SetStatus(incidentId, userId, IncidentStatus.Ignored, reason, ct);

    private async Task SetStatus(
        Guid incidentId,
        Guid userId,
        string status,
        string notes,
        CancellationToken ct)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                update ged.system_incident
                set status = @Status,
                    resolved_by = @UserId,
                    resolved_at = now(),
                    resolution_notes = @Notes
                where id = @IncidentId
                """,
                new
                {
                    IncidentId = incidentId,
                    UserId = userId,
                    Status = status,
                    Notes = notes
                },
                tx,
                cancellationToken: ct));

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                insert into ged.system_incident_event(
                    incident_id,
                    event_type,
                    event_message,
                    user_id
                )
                values(
                    @IncidentId,
                    @EventType,
                    @Message,
                    @UserId
                )
                """,
                new
                {
                    IncidentId = incidentId,
                    EventType = status,
                    Message = notes,
                    UserId = userId
                },
                tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task RecordAsync(RouteHealthRecordCommand command, CancellationToken ct)
    {
        await using var connection = await _db.OpenAsync(ct);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                insert into ged.route_health_snapshot(
                    route_path,
                    http_method,
                    expected_status,
                    actual_status,
                    success,
                    duration_ms,
                    error_message,
                    correlation_id,
                    payload_json
                )
                values(
                    @RoutePath,
                    @HttpMethod,
                    @ExpectedStatus,
                    @ActualStatus,
                    @Success,
                    @DurationMs,
                    @ErrorMessage,
                    @CorrelationId,
                    cast(@PayloadJson as jsonb)
                )
                """,
                new
                {
                    command.RoutePath,
                    command.HttpMethod,
                    command.ExpectedStatus,
                    command.ActualStatus,
                    command.Success,
                    command.DurationMs,
                    command.ErrorMessage,
                    command.CorrelationId,
                    PayloadJson = Redact(command.PayloadJson)
                },
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RouteHealthListItem>> ListAsync(CancellationToken ct)
    {
        await using var connection = await _db.OpenAsync(ct);
        var rows = await connection.QueryAsync<RouteHealthDbRow>(
            new CommandDefinition(
                """
                select id, route_path "RoutePath", http_method "HttpMethod",
                       expected_status "ExpectedStatus", actual_status "ActualStatus", success,
                       duration_ms "DurationMs", checked_at "CheckedAt",
                       error_message "ErrorMessage", correlation_id "CorrelationId"
                from ged.route_health_snapshot
                where reg_status = 'A'
                order by checked_at desc
                limit 250
                """,
                cancellationToken: ct));

        return rows.Select(row => row.ToModel()).ToList();
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"\b[0-9a-f]{8,}\b|\b\d+\b", "#");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? Redact(string? value) =>
        value is null
            ? null
            : Regex.Replace(
                value,
                "(?i)(password|token|secret|connectionstring)\\s*[:=]\\s*[^,;}]+",
                "$1=***");

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        var dateTime = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value;

        return new DateTimeOffset(dateTime);
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) =>
        value.HasValue ? ToDateTimeOffset(value.Value) : null;

    private sealed class SystemIncidentListDbRow
    {
        public Guid Id { get; set; }
        public string IncidentType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Status { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? RecommendedAction { get; set; }
        public string? Controller { get; set; }
        public string? Action { get; set; }
        public string? Path { get; set; }
        public string? CorrelationId { get; set; }
        public int OccurrenceCount { get; set; }
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }

        public SystemIncidentListItem ToModel() => new(
            Id, IncidentType, Severity, Status, Title, Message, RecommendedAction, Controller,
            Action, Path, CorrelationId, OccurrenceCount, ToDateTimeOffset(FirstSeenAt),
            ToDateTimeOffset(LastSeenAt));
    }

    private sealed class SystemIncidentEventDbRow
    {
        public Guid Id { get; set; }
        public string EventType { get; set; } = "";
        public string EventMessage { get; set; } = "";
        public DateTime OccurredAt { get; set; }
        public string? CorrelationId { get; set; }
        public string? UserName { get; set; }

        public SystemIncidentEvent ToModel() => new(
            Id, EventType, EventMessage, ToDateTimeOffset(OccurredAt), CorrelationId, UserName);
    }

    private sealed class RouteHealthDbRow
    {
        public Guid Id { get; set; }
        public string RoutePath { get; set; } = "";
        public string HttpMethod { get; set; } = "";
        public string? ExpectedStatus { get; set; }
        public int? ActualStatus { get; set; }
        public bool Success { get; set; }
        public int? DurationMs { get; set; }
        public DateTime CheckedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public string? CorrelationId { get; set; }

        public RouteHealthListItem ToModel() => new(
            Id, RoutePath, HttpMethod, ExpectedStatus, ActualStatus, Success, DurationMs,
            ToDateTimeOffset(CheckedAt), ErrorMessage, CorrelationId);
    }

    private sealed class DetailDbRow
    {
        public Guid Id { get; set; }
        public string IncidentType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Status { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? TechnicalMessage { get; set; }
        public string? RecommendedAction { get; set; }
        public string? RecommendedScript { get; set; }
        public string? Controller { get; set; }
        public string? Action { get; set; }
        public string? Path { get; set; }
        public string? HttpMethod { get; set; }
        public string? CorrelationId { get; set; }
        public string? SqlState { get; set; }
        public string? DatabaseObject { get; set; }
        public string? ExceptionType { get; set; }
        public string? StackTrace { get; set; }
        public int OccurrenceCount { get; set; }
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        public Guid? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionNotes { get; set; }

        public SystemIncidentDetails ToModel(IReadOnlyList<SystemIncidentEvent> events) => new(
            Id, IncidentType, Severity, Status, Title, Message, TechnicalMessage,
            RecommendedAction, RecommendedScript, Controller, Action, Path, HttpMethod,
            CorrelationId, SqlState, DatabaseObject, ExceptionType, StackTrace, OccurrenceCount,
            ToDateTimeOffset(FirstSeenAt), ToDateTimeOffset(LastSeenAt), ResolvedBy,
            ToDateTimeOffset(ResolvedAt), ResolutionNotes, events);
    }
}
