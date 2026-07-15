using InovaGed.Application;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebGed.WebApi.Controllers;

namespace InovaGed.Application.Tests.WebApi;

public sealed class WebApiDependencyInjectionTests
{
    [Fact]
    public void WebApi_ServiceProvider_BuildsSuccessfully_WithValidation()
    {
        var provider = BuildProvider();
        Assert.NotNull(provider.GetRequiredService<DocumentAppService>());
        Assert.NotNull(provider.GetRequiredService<IDocumentMoveService>());
        Assert.NotNull(provider.GetRequiredService<IDocumentGuardianService>());
    }

    [Fact]
    public void WebApi_Controllers_AreResolvableByContainer()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var controllerTypes = typeof(DocumentsController).Assembly.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToArray();

        Assert.NotEmpty(controllerTypes);
        foreach (var controllerType in controllerTypes)
        {
            Assert.NotNull(ActivatorUtilities.CreateInstance(scope.ServiceProvider, controllerType));
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, FakeCurrentUser>();
        var configuration = BuildConfiguration();
        services.AddInovaGedApplication(configuration);
        services.AddInovaGedInfrastructure(configuration);
        services.AddControllers().AddApplicationPart(typeof(DocumentsController).Assembly);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=inovaged_test;Username=test;Password=test",
            ["Storage:Local:RootPath"] = "App_Data/TestStorage",
            ["Preview:LibreOfficePath"] = "soffice"
        })
        .Build();

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid TenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid UserId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public string Email => "di-tests@inovaged.local";
        public IReadOnlyList<string> Roles { get; } = new[] { "Administrador" };
    }
}
