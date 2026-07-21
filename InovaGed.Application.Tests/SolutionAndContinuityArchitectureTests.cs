using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests;

public sealed class SolutionAndContinuityArchitectureTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void Solution_has_unique_projects_and_no_content_after_EndGlobal()
    {
        var sln = File.ReadAllText(Path.Combine(Root, "InovaGed.sln"));
        Assert.EndsWith("EndGlobal\n", sln.Replace("\r\n", "\n"));
        var after = sln[(sln.LastIndexOf("EndGlobal", StringComparison.Ordinal) + "EndGlobal".Length)..].Trim();
        Assert.Equal(string.Empty, after);
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
    public void Guardrails_are_source_based_not_format_based()
    {
        var infra = File.ReadAllText(Path.Combine(Root, "InovaGed.Infrastructure/DependencyInjection.cs"));
        Assert.Contains("IStartupConfigurationValidator", infra);
        Assert.Contains("ISecretMasker", infra);
        Assert.Contains("IExecutableResolver", infra);
        var domain = File.ReadAllText(Path.Combine(Root, "InovaGed.Domain/InovaGed.Domain.csproj"));
        Assert.DoesNotContain("Npgsql", domain);
    }

    [Fact]
    public void Continuity_security_queries_are_tenant_scoped()
    {
        var permission = File.ReadAllText(Path.Combine(Root, "InovaGed.Application/Administration/PermissionEnforcement.cs"));
        Assert.Contains("u.tenant_id = @tenantId", permission);
        Assert.DoesNotContain("Task.FromResult(false)", permission);
        var auth = File.ReadAllText(Path.Combine(Root, "InovaGed.Infrastructure/Auth/AuthRepository.cs"));
        Assert.Contains("WHERE ur.tenant_id = @tenantId", auth);
    }
}
