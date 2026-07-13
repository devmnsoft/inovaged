using InovaGed.Application.Common.Time;
using Microsoft.Extensions.Configuration;

namespace InovaGed.Infrastructure.Common.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class TenantTimeZoneService : ITenantTimeZoneService
{
    public string FallbackTimeZoneId { get; }

    public TenantTimeZoneService(IConfiguration configuration)
    {
        FallbackTimeZoneId = configuration.GetValue<string>("Localization:DefaultTimeZone")
            ?? configuration.GetValue<string>("App:LocalTimeZoneId")
            ?? "America/Belem";
    }

    public TimeZoneInfo Resolve(string? tenantTimeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(tenantTimeZoneId) ? FallbackTimeZoneId : tenantTimeZoneId;
        foreach (var candidate in BuildCandidates(id))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    public DateTimeOffset ToTenantLocal(DateTimeOffset utcDateTime, string? tenantTimeZoneId)
        => TimeZoneInfo.ConvertTime(utcDateTime.ToUniversalTime(), Resolve(tenantTimeZoneId));

    private static IEnumerable<string> BuildCandidates(string id)
    {
        yield return id;
        if (id == "America/Belem") yield return "E. South America Standard Time";
    }
}
