using InovaGed.Application.SystemHealth;
using InovaGed.Application;
using InovaGed.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public void InfrastructureModule_RegistersStartupConfigurationServices()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["Storage:Local:RootPath"] = Path.GetTempPath()
        });

        builder.Services
            .AddInovaGedApplication(builder.Configuration)
            .AddInovaGedInfrastructure(builder.Configuration);

        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<ISecretMasker>());
        Assert.NotNull(provider.GetRequiredService<IStartupConfigurationValidator>());
        Assert.NotNull(provider.GetRequiredService<IExecutableResolver>());
    }
}
