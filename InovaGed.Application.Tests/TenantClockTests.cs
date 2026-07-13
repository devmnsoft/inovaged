using InovaGed.Application.Common.Time;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class TenantClockTests
{
    [Fact]
    public void Deve_Converter_Utc_Para_America_Belem()
    {
        var clock = new SystemTenantClock();
        var local = clock.ToTenantLocal(new DateTime(2026, 7, 13, 3, 30, 0, DateTimeKind.Utc), "America/Belem");
        Assert.Equal(0, local.Hour);
        Assert.Equal(-3, local.Offset.Hours);
    }

    [Fact]
    public void Vencimento_Na_Virada_Do_Dia_Deve_Respeitar_Timezone_Do_Tenant()
    {
        var clock = new SystemTenantClock();
        var utc = clock.TenantLocalDateToUtc(new DateOnly(2026, 7, 14), new TimeOnly(0, 0), "America/Belem");
        Assert.Equal(new DateTime(2026, 7, 14, 3, 0, 0, DateTimeKind.Utc), utc);
    }
}
