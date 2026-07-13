namespace InovaGed.Application.Common.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ITenantTimeZoneService
{
    string FallbackTimeZoneId { get; }
    TimeZoneInfo Resolve(string? tenantTimeZoneId);
    DateTimeOffset ToTenantLocal(DateTimeOffset utcDateTime, string? tenantTimeZoneId);
}
