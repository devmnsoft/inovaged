using InovaGed.Application.SystemHealth;
using InovaGed.Application;
using InovaGed.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using InovaGed.Infrastructure.SystemHealth;

namespace InovaGed.Application.Tests;

public sealed class SystemHealthStartupTests
{
    [Fact]
    public void SecretMasker_MasksSecrets_AndTruncatesOutput()
    {
        var masker = new SecretMasker();

        var masked = masker.Mask("Host=localhost;Password=super-secret;Token=abc123;Key=value");

        Assert.DoesNotContain("super-secret", masked);
        Assert.DoesNotContain("abc123", masked);
        Assert.DoesNotContain("value", masked);
        Assert.Contains("Password=***", masked);
        Assert.Contains("Token=***", masked);
        Assert.Contains("Key=***", masked);
        Assert.True(masked.Length <= 180);
    }

    [Fact]
    public void ExecutableResolver_ReturnsUnavailable_ForMissingTool()
    {
        var resolver = new ExecutableResolver();

        var result = resolver.Resolve("definitely-missing-inovaged-tool", null, "INOVAGED_MISSING_TOOL", Array.Empty<string>());

        Assert.False(result.IsAvailable);
        Assert.Equal("PATH", result.Source);
    }
}

public sealed class InfrastructureHealthModuleRegistrationTests
{
    [Fact]
    public void Infrastructure_RegistersStartupConfigurationServices()
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=test;" +
                        "Username=test;Password=segura",

                    ["Storage:Local:RootPath"] =
                        Path.GetTempPath(),

                    ["Auth:AllowInternalSelfSignedCertificates"] =
                        "false",

                    ["SystemSeed:Enabled"] = "false"
                })
            .Build();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment("Development"));

        services
            .AddInovaGedApplication(configuration)
            .AddInovaGedInfrastructure(configuration);

        using var provider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

        Assert.NotNull(
            provider.GetRequiredService<ISecretMasker>());

        Assert.NotNull(
            provider.GetRequiredService<
                IStartupConfigurationValidator>());

        Assert.NotNull(
            provider.GetRequiredService<IExecutableResolver>());
    }
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
        ApplicationName = "InovaGed.Application.Tests";
        ContentRootPath = Directory.GetCurrentDirectory();
        ContentRootFileProvider = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    public string EnvironmentName { get; set; }

    public string ApplicationName { get; set; }

    public string ContentRootPath { get; set; }

    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
}
