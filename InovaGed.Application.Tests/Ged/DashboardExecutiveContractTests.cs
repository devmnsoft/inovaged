namespace InovaGed.Application.Tests.Ged;

public sealed class DashboardExecutiveContractTests
{
    [Fact]
    public void Dashboard_uses_tenant_counts_and_links_real_lists()
    {
        var root = RepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "InovaGed.Infrastructure/Ged/Dashboard/GedDashboardService.cs"));
        var view = File.ReadAllText(Path.Combine(root, "InovaGed.Web/Views/GedDashboard/Index.cshtml"));
        Assert.Contains("where tenant_id=@tenantId", service, StringComparison.Ordinal);
        Assert.Contains("status::text in ('FAILED','ERROR')", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfidentialDocuments = 0", service, StringComparison.Ordinal);
        Assert.Contains("Previews com erro", view, StringComparison.Ordinal);
        Assert.Contains("/Ged/Processing?status=error&amp;lane=PREVIEW", view, StringComparison.Ordinal);
        Assert.Contains("name=\"period\"", view, StringComparison.Ordinal);
        Assert.Contains("Prazo arquivístico próximo", view, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("InovaGed.sln");
    }
}
