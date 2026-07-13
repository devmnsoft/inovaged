using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Configuration;

namespace InovaGed.Infrastructure.Common.Time;

public sealed class SystemApplicationClock : IApplicationClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class ConfigurationTenantTimeZoneProvider : ITenantTimeZoneProvider
{
    public const string FallbackTimeZone = "America/Belem";
    private readonly IConfiguration _configuration;
    public ConfigurationTenantTimeZoneProvider(IConfiguration configuration) => _configuration = configuration;
    public string GetTimeZoneId(Guid? tenantId = null) =>
        _configuration[$"Tenants:{tenantId}:TimeZone"]
        ?? _configuration["Localization:DefaultTimeZone"]
        ?? _configuration["App:LocalTimeZoneId"]
        ?? FallbackTimeZone;
}

public sealed class TenantDateTimeZoneConverter : IDateTimeZoneConverter
{
    private readonly ITenantTimeZoneProvider _provider;
    public TenantDateTimeZoneConverter(ITenantTimeZoneProvider provider) => _provider = provider;
    public DateTimeOffset ToTenantLocal(DateTimeOffset utc, Guid? tenantId = null)
    {
        var value = utc.ToUniversalTime();
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(_provider.GetTimeZoneId(tenantId)); }
        catch { zone = TimeZoneInfo.FindSystemTimeZoneById(ConfigurationTenantTimeZoneProvider.FallbackTimeZone); }
        return TimeZoneInfo.ConvertTime(value, zone);
    }
}
