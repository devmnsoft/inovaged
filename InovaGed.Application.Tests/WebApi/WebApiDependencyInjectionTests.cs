using InovaGed.Application.Documents;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebGed.WebApi.Controllers;

namespace InovaGed.Application.Tests.WebApi;

public sealed class WebApiDependencyInjectionTests
{
    [Fact]
    public void WebApi_ServiceProvider_BuildsSuccessfully_WithValidation()
    {
        var services = BuildServices();

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

    [Fact]
    public void WebApi_AllControllers_AreResolvedByContainer()
    {
        var services = BuildServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using var scope = provider.CreateScope();
        var controllerTypes = typeof(DocumentsController).Assembly
            .GetTypes()
            .Where(type => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .OrderBy(type => type.FullName)
            .ToArray();

        Assert.NotEmpty(controllerTypes);
        foreach (var controllerType in controllerTypes)
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(controllerType));
        }
    }

    private static IServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=inovaged_tests;Username=inovaged;Password=inovaged",
                ["Storage:Local:RootPath"] = "App_Data/TestGedStorage",
                ["Preview:SofficePath"] = "/usr/bin/soffice",
                ["Ocr:PdfToTextPath"] = "/usr/bin/pdftotext"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, FakeCurrentUser>();
        services.AddControllers()
            .AddApplicationPart(typeof(DocumentsController).Assembly)
            .AddControllersAsServices();
        services
            .AddInovaGedApplication(configuration)
            .AddInovaGedInfrastructure(configuration);
        return services;
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid TenantId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public Guid UserId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000002");
        public string Email => "di-tests@inovaged.local";
        public IReadOnlyList<string> Roles { get; } = new[] { "ADMIN" };
    }
}
