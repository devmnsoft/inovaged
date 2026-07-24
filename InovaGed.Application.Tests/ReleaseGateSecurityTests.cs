using InovaGed.Application;
using InovaGed.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class ReleaseGateSecurityTests
{
    [Fact]
    public void ProductionTrees_DoNotContainRemovedStubs()
    {
        var repo = FindRepositoryRoot();
        foreach (var relativeRoot in new[] { "InovaGed.Web", "InovaGed.Infrastructure", "WebGed.WebApi" })
        {
            var root = Path.Combine(repo, relativeRoot);
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("AllowAllPermissionChecker", text);
                Assert.DoesNotContain("CertificateValidationStub", text);
            }
        }
    }

    [Fact]
    public void SingleResolutionInterfaces_AreNotRegisteredMoreThanOnce()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=inovaged_tests;Username=test;Password=test",
            ["Storage:Local:RootPath"] = Path.GetTempPath(),
            ["Preview:LibreOfficePath"] = "soffice"
        }).Build();

        services.AddInovaGedApplication(configuration);
        services.AddInovaGedInfrastructure(configuration);
        var guarded = new[]
        {
            "InovaGed.Application.Common.Storage.IFileStorage",
            "InovaGed.Application.Identity.IPermissionChecker",
            "InovaGed.Application.Ocr.IOcrService",
            "InovaGed.Application.Signatures.ISigningOrchestrator",
            "InovaGed.Application.Signatures.ISignatureValidationService"
        };

        foreach (var serviceName in guarded)
            Assert.True(services.Count(d => d.ServiceType.FullName == serviceName) <= 1, serviceName);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("InovaGed.sln not found.");
    }
}
