using InovaGed.Application.Signatures;
using InovaGed.Infrastructure;
using InovaGed.Infrastructure.Signatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InovaGed.Application.Tests;

public sealed class SignatureInterfaceCompatibilityTests
{
    [Theory]
    [InlineData(false, typeof(NotConfiguredSignatureValidationService), typeof(NotConfiguredSigningOrchestrator))]
    [InlineData(true, typeof(CmsDetachedSignatureValidationService), typeof(CmsSigningOrchestrator))]
    [Trait("Category", "Architecture")]
    [Trait("Category", "CmsContract")]
    public void Digital_signature_configuration_registers_one_matching_production_pair(
        bool enabled,
        Type expectedValidator,
        Type expectedOrchestrator)
    {
        var configuration = CreateConfiguration(enabled);
        var services = new ServiceCollection();

        services.AddInovaGedInfrastructure(configuration);

        AssertSingleRegistration<ISignatureValidationService>(services, expectedValidator);
        AssertSingleRegistration<ISigningOrchestrator>(services, expectedOrchestrator);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType(expectedValidator, scope.ServiceProvider.GetRequiredService<ISignatureValidationService>());
        Assert.IsType(expectedOrchestrator, scope.ServiceProvider.GetRequiredService<ISigningOrchestrator>());
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Signature_contract_implementations_are_concrete()
    {
        Type[] contracts =
        [
            typeof(ISignatureValidationService),
            typeof(ISigningOrchestrator),
            typeof(ISignatureRepository),
            typeof(ISignatureValidationRepository),
            typeof(ISignaturePackageService),
            typeof(ICertificateIdentityService)
        ];
        var assemblies = new[] { typeof(CmsDetachedSignatureValidationService).Assembly };

        foreach (var contract in contracts)
        {
            var implementations = assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass && contract.IsAssignableFrom(type))
                .ToArray();

            Assert.NotEmpty(implementations);
            Assert.DoesNotContain(implementations, type => type.IsAbstract);
        }
    }

    private static IConfiguration CreateConfiguration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=inovaged;Username=test;Password=test",
                ["DigitalSignature:Enabled"] = enabled.ToString(),
                ["DigitalSignature:Mode"] = "AgentCms",
                ["DigitalSignature:RequireCertificateIdentityMatch"] = "false",
                ["Storage:Provider"] = "Local",
                ["Storage:Local:RootPath"] = Path.GetTempPath()
            })
            .Build();

    private static void AssertSingleRegistration<TContract>(IServiceCollection services, Type implementation)
    {
        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(TContract)).ToArray();
        var registration = Assert.Single(registrations);
        Assert.Equal(implementation, registration.ImplementationType);
    }
}
