using System.Reflection;
using InovaGed.Application;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.Ged.Documents;
using InovaGed.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InovaGed.Application.Tests;

public sealed class ModuleArchitectureTests
{
    [Fact]
    public void DependencyInjectionExtensions_DoNotExposeAmbiguousApplicationMethod()
    {
        var methods = typeof(ApplicationServiceCollectionExtensions).Assembly.GetTypes()
            .Concat(typeof(InfrastructureServiceCollectionExtensions).Assembly.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name == "AddInovaGedApplication")
            .ToArray();

        Assert.Single(methods);
        Assert.Equal(typeof(ApplicationServiceCollectionExtensions), methods[0].DeclaringType);
    }

    [Fact]
    public void ApplicationAndInfrastructure_ResolveCriticalServices_WithoutDuplicateHostedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInovaGedApplication(Configuration());
        services.AddInovaGedInfrastructure(Configuration());

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToArray();
        Assert.Equal(hosted.Length, hosted.Select(d => d.ImplementationType ?? d.ImplementationInstance?.GetType()).Distinct().Count());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentMoveService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentGuardianService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuditWriter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPreviewGenerator>());
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=inovaged_tests;Username=test;Password=test",
        ["Storage:Local:RootPath"] = Path.GetTempPath(),
        ["Preview:LibreOfficePath"] = "soffice"
    }).Build();
}
