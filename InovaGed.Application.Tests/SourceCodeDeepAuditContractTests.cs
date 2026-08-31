using System.Text.Json;

namespace InovaGed.Application.Tests;

public sealed class SourceCodeDeepAuditContractTests
{
    [Fact]
    public void Critical_operational_routes_are_in_doctor_manifest()
    {
        var routes = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Root("InovaGed.Environment.Doctor/quality-routes.json")))!;
        foreach (var route in new[] { "/Dashboard", "/GlobalSearch", "/Administration/Consistency", "/SmartWorkflow", "/Governance", "/FiscalPortal" })
            Assert.Contains(route, routes);
    }

    [Fact]
    public void Upload_consistency_uses_mutable_database_row_and_explicit_utc_mapping()
    {
        var source = File.ReadAllText(Root("InovaGed.Infrastructure/Ged/Documents/UploadBatchConsistencyService.cs"));
        Assert.Contains("QuerySingleOrDefaultAsync<UploadBatchConsistencyDbRow>", source);
        Assert.Contains("DateTime.SpecifyKind", source);
        Assert.DoesNotContain("QuerySingleOrDefaultAsync<UploadBatchConsistencyResult>", source);
    }

    [Fact]
    public void Consistency_report_is_read_only_schema_aware_and_tenant_scoped()
    {
        var source = File.ReadAllText(Root("InovaGed.Infrastructure/Administration/ConsistencyAuditService.cs"));
        Assert.Contains("to_regclass", source);
        Assert.Contains("information_schema.columns", source);
        Assert.Contains("tenant_id=@tenantId", source);
        Assert.DoesNotContain("delete from", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update ged.", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string Root(string path) => GlobalJsonContractTests.Root(path);
}
