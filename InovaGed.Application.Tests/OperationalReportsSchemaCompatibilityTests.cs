using Xunit;

namespace InovaGed.Application.Tests;

public sealed class OperationalReportsSchemaCompatibilityTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Document_export_introspects_legacy_columns_before_building_sql()
    {
        var controller = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/Controller/ReportsController.cs"));

        Assert.Contains("if (!schema.HasDocuments)", controller);
        Assert.Contains("schema.HasDocumentStatus", controller);
        Assert.Contains("schema.HasDocumentCreatedAt", controller);
        Assert.Contains("schema.HasFolderName", controller);
        Assert.Contains("TempData[\"Warning\"]", controller);
        Assert.Contains("null::timestamp", controller);
    }

    [Fact]
    public void Document_export_preserves_tenant_isolation_and_audit()
    {
        var controller = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/Controller/ReportsController.cs"));

        Assert.Contains("where d.tenant_id=@tenant", controller);
        Assert.Contains("tenant = TenantId", controller);
        Assert.Contains("REPORT_EXPORTED", controller);
        Assert.Contains("limit 50000", controller);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
