namespace InovaGed.Web.Models;

public sealed record MobileSyncRequest(string ClientSyncId,string? DeviceId,IReadOnlyList<MobileSyncItem>? Items);
public sealed record MobileSyncItem(string ClientId,string Payload,string? SubjectType,Guid? SubjectId,string? ControlNumber,string? ExpectedLocation,string? FoundLocation,Guid? InventorySessionId,DateTimeOffset? CapturedAt,decimal? Latitude,decimal? Longitude);
public sealed record MobileSyncItemResult(string ClientId,string Status,string Message,Guid? EventId=null);
public sealed record MobileSyncResponse(string ClientSyncId,int Accepted,int Rejected,IReadOnlyList<MobileSyncItemResult> Items);
public sealed record MobileMoveRequest(string Payload,string NewLocation,string Reason,string? Observation,string? DeviceId);
