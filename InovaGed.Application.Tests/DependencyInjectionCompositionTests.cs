using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Infrastructure;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class DiCompositionTests
{
    [Fact]
    public void SharedComposition_Builds_WithScopeAndBuildValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        var configuration = CreateConfiguration();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInovaGedApplication(configuration);
        services.AddInovaGedInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DocumentAppService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentMoveService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentGuardianService>());
        Assert.NotNull(provider.GetRequiredService<ISecretMasker>());
        Assert.NotNull(provider.GetRequiredService<IStartupConfigurationValidator>());
        Assert.NotNull(provider.GetRequiredService<IExecutableResolver>());
        Assert.NotNull(provider.GetRequiredService<IDbConnectionFactory>());
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

public sealed class DiArchitectureTests
{
    [Fact]
    public void DiExtensionMethods_DoNotExposeAmbiguousApplicationRegistration()
    {
        var extensionMethods = new[]
        {
            typeof(InovaGed.Application.ApplicationServiceCollectionExtensions),
            typeof(InovaGed.Infrastructure.InfrastructureServiceCollectionExtensions)
        }.SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
         .Where(method => method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false))
         .Select(method => new
         {
             method.Name,
             Parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName))
         })
         .ToArray();

        Assert.Single(extensionMethods, method => method.Name == "AddInovaGedApplication");
        Assert.Single(extensionMethods, method => method.Name == "AddInovaGedInfrastructure");
        Assert.DoesNotContain(extensionMethods.GroupBy(method => (method.Name, method.Parameters)), group => group.Count() > 1);
    }

    [Fact]
    public void InfrastructureComposition_ExposesInternalModuleCatalog()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=inovaged_tests;Username=test;Password=test",
                ["Storage:Local:RootPath"] = Path.GetTempPath(),
                ["Preview:LibreOfficePath"] = "soffice"
            })
            .Build();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        services.AddInovaGedInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        var catalog = provider.GetRequiredService<InovaGed.Infrastructure.IInfrastructureModuleCatalog>();
        var modules = catalog.GetModules();

        Assert.Contains(modules, module => module.Name == "Database");
        Assert.Contains(modules, module => module.Name == "GED");
        Assert.Contains(modules, module => module.Name == "OCR");
        Assert.Contains(modules, module => module.Name == "Preview");
        Assert.Contains(modules, module => module.Name == "Guardian");
    }
}

internal sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "InovaGed.Tests";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
