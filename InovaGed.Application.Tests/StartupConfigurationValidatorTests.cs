using InovaGed.Application.SystemHealth;
using InovaGed.Infrastructure.Common.Time;
using InovaGed.Infrastructure.SystemHealth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class StartupConfigurationValidatorTests
{
    [Fact]
    public void Missing_connection_string_is_critical()
    {
        var validator = CreateValidator(new Dictionary<string,string?>(), "Development");
        Assert.Contains(validator.Validate(), c => c.Item == "ConnectionStrings:DefaultConnection" && c.Severity == StartupConfigurationSeverity.Critical);
    }

    [Fact]
    public void Default_password_in_production_is_critical_and_masked()
    {
        var validator = CreateValidator(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = "Host=db;Username=postgres;Password=123456" }, "Production");
        var check = Assert.Single(validator.Validate(), c => c.Item == "Senha padrão PostgreSQL");
        Assert.Equal("Password=***", check.MaskedValue);
        Assert.DoesNotContain("123456", check.MaskedValue);
    }

    [Fact]
    public void Seed_and_self_signed_certificate_are_blocked_in_production()
    {
        var validator = CreateValidator(new Dictionary<string,string?> {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Username=app;Password=complexa",
            ["SystemSeed:Enabled"] = "true",
            ["Auth:AllowInternalSelfSignedCertificates"] = "true"
        }, "Production");
        var checks = validator.Validate();
        Assert.Contains(checks, c => c.Item == "Seed habilitado" && c.Severity == StartupConfigurationSeverity.Critical);
        Assert.Contains(checks, c => c.Item == "Certificado interno autoassinado permitido" && c.Severity == StartupConfigurationSeverity.Critical);
    }

    [Fact]
    public void Secret_masker_does_not_expose_passwords_tokens_or_keys()
    {
        var masked = new SecretMasker().Mask("Host=db;Password=abc;Token=def;Key=ghi");
        Assert.DoesNotContain("abc", masked);
        Assert.DoesNotContain("def", masked);
        Assert.DoesNotContain("ghi", masked);
    }

    private static StartupConfigurationValidator CreateValidator(Dictionary<string,string?> values, string env)
        => new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new FakeEnvironment(env), new SecretMasker());

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

public sealed class TimeZoneTests
{
    [Fact]
    public void Default_timezone_falls_back_to_belem()
    {
        var provider = new ConfigurationTenantTimeZoneProvider(new ConfigurationBuilder().Build());
        Assert.Equal("America/Belem", provider.GetTimeZoneId());
    }

    [Fact]
    public void Tenant_timezone_overrides_default()
    {
        var tenant = Guid.NewGuid();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { [$"Tenants:{tenant}:TimeZone"] = "America/Manaus" }).Build();
        Assert.Equal("America/Manaus", new ConfigurationTenantTimeZoneProvider(cfg).GetTimeZoneId(tenant));
    }

    [Fact]
    public void Utc_to_belem_conversion_uses_minus_three_offset()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Localization:DefaultTimeZone"] = "America/Belem" }).Build();
        var converted = new TenantDateTimeZoneConverter(new ConfigurationTenantTimeZoneProvider(cfg)).ToTenantLocal(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(9, converted.Hour);
        Assert.Equal(TimeSpan.FromHours(-3), converted.Offset);
    }
}
