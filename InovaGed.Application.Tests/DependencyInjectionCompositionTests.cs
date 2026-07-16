using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class DependencyInjectionCompositionTests
{
    [Fact]
    public void SharedComposition_Builds_WithScopeAndBuildValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddInovaGedApplication(CreateConfiguration());
        services.AddInovaGedInfrastructure(CreateConfiguration());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DocumentAppService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentMoveService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentGuardianService>());
    }

    private static IConfiguration CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=inovaged_tests;Username=test;Password=test",
            ["Storage:Local:RootPath"] = Path.GetTempPath(),
            ["Preview:LibreOfficePath"] = "soffice"
        })
        .Build();
}
