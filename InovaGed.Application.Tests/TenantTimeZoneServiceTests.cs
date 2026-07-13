using InovaGed.Infrastructure.Common.Time;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class TenantTimeZoneServiceTests
{
    [Fact]
    public void Resolve_UsesAmericaBelemFallback()
    {
        var service = new TenantTimeZoneService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Localization:DefaultTimeZone"] = "America/Belem"
        }).Build());

        Assert.Equal("America/Belem", service.FallbackTimeZoneId);
        Assert.NotNull(service.Resolve(null));
    }

    [Fact]
    public void ToTenantLocal_ConvertsFromUtcWithoutChangingInstant()
    {
        var service = new TenantTimeZoneService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Localization:DefaultTimeZone"] = "America/Belem"
        }).Build());
        var utc = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

        var local = service.ToTenantLocal(utc, null);

        Assert.Equal(utc, local.ToUniversalTime());
        Assert.Equal(-3, local.Offset.Hours);
    }
}
