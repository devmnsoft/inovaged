using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests;

public sealed class SolutionAndContinuityArchitectureTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void Solution_has_unique_projects_and_required_projects()
    {
        var sln = File.ReadAllText(Path.Combine(Root, "InovaGed.sln"));
        var projects = Regex.Matches(sln, "Project\\(.*?= \\\"(?<name>[^\\\"]+)\\\", \\\"(?<path>[^\\\"]+\\.csproj)\\\"")
            .Select(m => m.Groups["path"].Value.Replace('\\', '/')).ToList();

        Assert.Equal(projects.Count, projects.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var expected in new[]
        {
            "InovaGed.Domain/InovaGed.Domain.csproj",
            "InovaGed.Application/InovaGed.Application.csproj",
            "InovaGed.Infrastructure/InovaGed.Infrastructure.csproj",
            "InovaGed.Web/InovaGed.Web.csproj",
            "WebGed.WebApi/WebGed.WebApi.csproj",
            "InovaGed.Application.Tests/InovaGed.Application.Tests.csproj",
            "InovaGed.Operations.Worker/InovaGed.Operations.Worker.csproj",
            "InovaGed.Portability.Verifier/InovaGed.Portability.Verifier.csproj"
        }) Assert.Contains(expected, projects);
    }

    [Fact]
    public void Layering_keeps_database_implementation_out_of_domain_and_permission_contract_out_of_application_sql()
    {
        var domain = File.ReadAllText(Path.Combine(Root, "InovaGed.Domain/InovaGed.Domain.csproj"));
        Assert.DoesNotContain("Npgsql", domain);

        var permissionContract = File.ReadAllText(Path.Combine(Root, "InovaGed.Application/Administration/PermissionEnforcement.cs"));
        Assert.DoesNotContain("using Dapper", permissionContract);
        Assert.DoesNotContain("select exists", permissionContract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IRealPermissionChecker", permissionContract);

        var permissionImplementation = File.ReadAllText(Path.Combine(Root, "InovaGed.Infrastructure/Security/DatabasePermissionChecker.cs"));
        Assert.Contains("u.tenant_id = @tenantId", permissionImplementation);
        Assert.Contains("join ged.user_role ur on ur.tenant_id = u.tenant_id", permissionImplementation);
    }

    [Fact]
    public void Continuity_dependency_injection_registers_required_services()
    {
        var infra = File.ReadAllText(Path.Combine(Root, "InovaGed.Infrastructure/DependencyInjection.cs"));
        Assert.Contains("IStartupConfigurationValidator", infra);
        Assert.Contains("ISecretMasker", infra);
        Assert.Contains("IExecutableResolver", infra);
        Assert.Contains("IAdministrativeTenantScopeResolver", infra);
        Assert.Contains("IPostgresBackupProvider", infra);
        Assert.Contains("IPortabilityPackageVerifier", infra);
    }
}
