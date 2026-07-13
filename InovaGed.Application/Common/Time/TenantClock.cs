namespace InovaGed.Application.Common.Time;

public interface ITenantClock
{
    DateTime UtcNow { get; }
    TimeZoneInfo GetTenantTimeZone(string? timeZoneId);
    DateTimeOffset ToTenantLocal(DateTime utcDateTime, string? timeZoneId);
    DateTime TenantLocalDateToUtc(DateOnly localDate, TimeOnly localTime, string? timeZoneId);
}

public sealed class SystemTenantClock : ITenantClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public TimeZoneInfo GetTenantTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
    public DateTimeOffset ToTenantLocal(DateTime utcDateTime, string? timeZoneId)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc ? utcDateTime : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), GetTenantTimeZone(timeZoneId));
    }
    public DateTime TenantLocalDateToUtc(DateOnly localDate, TimeOnly localTime, string? timeZoneId)
    {
        var local = localDate.ToDateTime(localTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, GetTenantTimeZone(timeZoneId));
    }
}
