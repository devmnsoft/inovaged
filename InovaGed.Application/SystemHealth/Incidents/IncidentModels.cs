using Microsoft.AspNetCore.Http;

namespace InovaGed.Application.SystemHealth.Incidents;

public static class IncidentType
{
    public const string DatabaseSchemaMissingTable = "DATABASE_SCHEMA_MISSING_TABLE";
    public const string DatabaseSchemaMissingColumn = "DATABASE_SCHEMA_MISSING_COLUMN";
    public const string DatabaseSqlSyntax = "DATABASE_SQL_SYNTAX";
    public const string DapperMaterialization = "DAPPER_MATERIALIZATION";
    public const string DependencyInjection = "DEPENDENCY_INJECTION";
    public const string RazorCompilation = "RAZOR_COMPILATION";
    public const string MigrationPending = "MIGRATION_PENDING";
    public const string RouteFailure = "ROUTE_FAILURE";
    public const string PermissionFailure = "PERMISSION_FAILURE";
    public const string OperationCancelled = "OPERATION_CANCELLED";
    public const string IconMissing = "ICON_MISSING";
    public const string Unknown = "UNKNOWN";
}
public static class IncidentSeverity { public const string Critical="CRITICAL"; public const string High="HIGH"; public const string Medium="MEDIUM"; public const string Low="LOW"; public const string Info="INFO"; }
public static class IncidentStatus { public const string Open="OPEN"; public const string InReview="IN_REVIEW"; public const string Resolved="RESOLVED"; public const string Ignored="IGNORED"; }

public sealed record SystemIncidentClassification(string IncidentType,string Severity,string Title,string RecommendedAction,string? SqlState=null,string? DatabaseObject=null);
public sealed record SystemIncidentCreateCommand(string IncidentType,string Severity,string Title,string Message,string? TechnicalMessage=null,string? RecommendedAction=null,string? RecommendedScript=null,Guid? TenantId=null,string? CorrelationId=null,string? Controller=null,string? Action=null,string? Path=null,string? HttpMethod=null,string? SqlState=null,string? DatabaseObject=null,string? ExceptionType=null,string? StackTrace=null,string? PayloadJson=null);
public sealed record SystemIncidentFilter(string? Type=null,string? Severity=null,string? Status=null,string? Controller=null,string? Action=null,string? Path=null,string? CorrelationId=null,DateTimeOffset? From=null,DateTimeOffset? To=null,Guid? TenantId=null,int Limit=250);
public sealed record SystemIncidentListItem(Guid Id,string IncidentType,string Severity,string Status,string Title,string Message,string? RecommendedAction,string? Controller,string? Action,string? Path,string? CorrelationId,int OccurrenceCount,DateTimeOffset FirstSeenAt,DateTimeOffset LastSeenAt);
public sealed record SystemIncidentEvent(Guid Id,string EventType,string EventMessage,DateTimeOffset OccurredAt,string? CorrelationId,string? UserName);
public sealed record SystemIncidentDetails(Guid Id,string IncidentType,string Severity,string Status,string Title,string Message,string? TechnicalMessage,string? RecommendedAction,string? RecommendedScript,string? Controller,string? Action,string? Path,string? HttpMethod,string? CorrelationId,string? SqlState,string? DatabaseObject,string? ExceptionType,string? StackTrace,int OccurrenceCount,DateTimeOffset FirstSeenAt,DateTimeOffset LastSeenAt,Guid? ResolvedBy,DateTimeOffset? ResolvedAt,string? ResolutionNotes,IReadOnlyList<SystemIncidentEvent> Events);
public sealed record RouteHealthRecordCommand(string RoutePath,string HttpMethod,string? ExpectedStatus,int? ActualStatus,bool Success,int? DurationMs,string? ErrorMessage,string? CorrelationId,string? PayloadJson=null);
public sealed record RouteHealthListItem(Guid Id,string RoutePath,string HttpMethod,string? ExpectedStatus,int? ActualStatus,bool Success,int? DurationMs,DateTimeOffset CheckedAt,string? ErrorMessage,string? CorrelationId);

public interface ISystemIncidentService { Task<Guid> RegisterAsync(SystemIncidentCreateCommand command,CancellationToken ct); Task<IReadOnlyList<SystemIncidentListItem>> ListAsync(SystemIncidentFilter filter,CancellationToken ct); Task<SystemIncidentDetails?> GetAsync(Guid incidentId,CancellationToken ct); Task ResolveAsync(Guid incidentId,Guid userId,string notes,CancellationToken ct); Task IgnoreAsync(Guid incidentId,Guid userId,string reason,CancellationToken ct); }
public interface IExceptionClassifier { SystemIncidentClassification Classify(Exception exception,HttpContext? httpContext=null); }
public interface IRouteHealthRecorder { Task RecordAsync(RouteHealthRecordCommand command,CancellationToken ct); Task<IReadOnlyList<RouteHealthListItem>> ListAsync(CancellationToken ct); }
