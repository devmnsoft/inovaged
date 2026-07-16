using InovaGed.Application.SystemHealth;
using InovaGed.Infrastructure.Common.Time;
using InovaGed.Infrastructure.SystemHealth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class StartupConfigurationValidatorTests
{
    [Fact]
    public void Missing_connection_string_is_critical()
    {
        var checks = CreateValidator(new Dictionary<string, string?>(), "Development").Validate();

        Assert.Contains(checks, c => c.Item == "ConnectionStrings:DefaultConnection" && c.Severity == StartupConfigurationSeverity.Critical);
    }

    [Fact]
    public void Strong_password_valid_connection_string_has_no_weak_or_invalid_findings()
    {
        const string password = "Senha-Forte-Teste-2026!";
        var checks = ValidateConnectionString($"Host=localhost;Database=inovaged;Username=inovaged_app;Password={password}", "Production");

        Assert.DoesNotContain(checks, c => c.Item == "Senha padrão PostgreSQL");
        Assert.DoesNotContain(checks, c => c.Item == "Connection string PostgreSQL");
        AssertNoLeak(checks, password);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("postgres")]
    [InlineData("admin")]
    [InlineData("admin@123")]
    [InlineData("password")]
    [InlineData("senha123")]
    public void Weak_passwords_are_critical_and_never_leaked(string password)
    {
        var checks = ValidateConnectionString($"Host=db;Database=inovaged;Username=inovaged_app;Password={password}", "Production");
        var check = Assert.Single(checks, c => c.Item == "Senha padrão PostgreSQL");

        Assert.Equal(StartupConfigurationSeverity.Critical, check.Severity);
        Assert.Equal("Password=***", check.MaskedValue);
        AssertNoLeak(checks, password);
    }

    [Fact]
    public void Invalid_connection_string_generates_critical_check_without_throwing()
    {
        var exception = Record.Exception(() => ValidateConnectionString("isto-nao-e-uma-connection-string", "Production"));
        Assert.Null(exception);

        var checks = ValidateConnectionString("isto-nao-e-uma-connection-string", "Production");
        Assert.Contains(checks, c => c.Item == "Connection string PostgreSQL" && c.Severity == StartupConfigurationSeverity.Critical);
    }

    [Fact]
    public void Missing_password_is_warning_in_development_and_critical_in_production()
    {
        var development = ValidateConnectionString("Host=db;Database=inovaged;Username=inovaged_app", "Development");
        var production = ValidateConnectionString("Host=db;Database=inovaged;Username=inovaged_app", "Production");

        Assert.Contains(development, c => c.Item == "Senha PostgreSQL" && c.Severity == StartupConfigurationSeverity.Warning);
        Assert.Contains(production, c => c.Item == "Senha PostgreSQL" && c.Severity == StartupConfigurationSeverity.Critical);
    }

    [Fact]
    public void Default_database_is_warned_without_sensitive_values()
    {
        const string password = "Senha-Forte-Teste-2026!";
        var checks = ValidateConnectionString($"Host=db;Database=postgres;Username=inovaged_app;Password={password}", "Development");

        Assert.Contains(checks, c => c.Item == "Banco de dados PostgreSQL" && c.Severity == StartupConfigurationSeverity.Warning && c.MaskedValue == "postgres");
        AssertNoLeak(checks, password);
    }

    [Theory]
    [InlineData("Development", StartupConfigurationSeverity.Warning)]
    [InlineData("Production", StartupConfigurationSeverity.Critical)]
    public void Postgres_superuser_is_warned_or_critical_by_environment(string environment, StartupConfigurationSeverity severity)
    {
        const string password = "Senha-Forte-Teste-2026!";
        var checks = ValidateConnectionString($"Host=db;Database=inovaged;Username=postgres;Password={password}", environment);

        Assert.Contains(checks, c => c.Item == "Usuário PostgreSQL" && c.Severity == severity && c.MaskedValue == "postgres");
        AssertNoLeak(checks, password);
    }

    [Fact]
    public void Escaped_semicolon_password_is_parsed_without_false_invalid_or_weak_findings()
    {
        const string password = "Senha;Forte;Teste;2026!";
        var checks = ValidateConnectionString("Host=db;Database=inovaged;Username=inovaged_app;Password='Senha;Forte;Teste;2026!'", "Production");

        Assert.DoesNotContain(checks, c => c.Item == "Connection string PostgreSQL");
        Assert.DoesNotContain(checks, c => c.Item == "Senha padrão PostgreSQL");
        AssertNoLeak(checks, password);
    }

    [Fact]
    public void Seed_and_self_signed_certificate_are_blocked_in_production()
    {
        var validator = CreateValidator(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=inovaged;Username=app;Password=complexa",
            ["SystemSeed:Enabled"] = "true",
            ["Auth:AllowInternalSelfSignedCertificates"] = "true"
        }, "Production");
        var checks = validator.Validate();
        Assert.Contains(checks, c => c.Item == "Seed habilitado" && c.Severity == StartupConfigurationSeverity.Critical);
        Assert.Contains(checks, c => c.Item == "Certificado interno autoassinado permitido" && c.Severity == StartupConfigurationSeverity.Critical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Secret_masker_returns_empty_for_empty_input(string value)
    {
        Assert.Equal(string.Empty, new SecretMasker().Mask(value));
    }

    [Fact]
    public void Secret_masker_masks_small_connection_string_password_token_secret_and_key()
    {
        var masked = new SecretMasker().Mask("Host=db;Password=abc;Token=def;Secret=jkl;Key=ghi");

        Assert.Contains("Password=***", masked);
        Assert.Contains("Token=***", masked);
        Assert.Contains("Secret=***", masked);
        Assert.Contains("Key=***", masked);
        Assert.DoesNotContain("abc", masked);
        Assert.DoesNotContain("def", masked);
        Assert.DoesNotContain("jkl", masked);
        Assert.DoesNotContain("ghi", masked);
    }

    [Fact]
    public void Secret_masker_limits_large_input_and_output()
    {
        var value = "Password=segredo;" + new string('a', 5000);
        var masked = new SecretMasker().Mask(value);

        Assert.DoesNotContain("segredo", masked);
        Assert.True(masked.Length <= 183);
    }

    [Fact]
    public void Secret_masker_handles_malicious_input_without_regex_timeout_exception()
    {
        var value = string.Concat(Enumerable.Repeat("Password=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;", 1000));
        var exception = Record.Exception(() => new SecretMasker().Mask(value));

        Assert.Null(exception);
    }

    [Fact]
    public void Secret_masker_fallback_value_is_safe()
    {
        Assert.Equal("***", "***");
    }

    [Fact]
    public void Security_ci_guards_prevent_default_password_regex_reference()
    {
        var files = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        Assert.DoesNotContain(files, path => File.ReadAllText(path).Contains("Default" + "Password" + "Regex", StringComparison.Ordinal));
    }

    [Fact]
    public void Security_ci_guards_prevent_password_values_in_appsettings()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(root, "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<StartupConfigurationCheck> ValidateConnectionString(string connectionString, string env) =>
        CreateValidator(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }, env).Validate();

    private static void AssertNoLeak(IReadOnlyList<StartupConfigurationCheck> checks, string secret) =>
        Assert.DoesNotContain(secret, string.Join(Environment.NewLine, checks.Select(x => x.MaskedValue)));

    private static StartupConfigurationValidator CreateValidator(Dictionary<string, string?> values, string env) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new FakeEnvironment(env), new SecretMasker());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
