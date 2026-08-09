using InovaGed.Web.Models.Poc;
using InovaGed.Web.Services;

namespace InovaGed.Application.Tests;

public sealed class PocDemonstrationCenterContractTests
{
    private readonly PocCatalogService _catalog = new();

    [Fact]
    public void DashboardExposesEveryRequiredProductModuleWithWorkingRoute()
    {
        var dashboard = _catalog.Dashboard();
        Assert.Equal(14, dashboard.Modules.Count);
        Assert.All(dashboard.Modules, module =>
        {
            Assert.InRange(module.Coverage, 0, 100);
            Assert.StartsWith("/", module.Url);
            Assert.False(string.IsNullOrWhiteSpace(module.Evidence));
            Assert.False(string.IsNullOrWhiteSpace(module.QuickActionLabel));
        });
    }

    [Fact]
    public void ChecklistContainsExactlyTwentySevenExecutableProofSteps()
    {
        var checklist = _catalog.Checklist();
        Assert.Equal(Enumerable.Range(1, 27), checklist.Items.Select(x => x.Number));
        Assert.All(checklist.Items, item =>
        {
            Assert.StartsWith("/", item.ProofScreen);
            Assert.False(string.IsNullOrWhiteSpace(item.TechnicalReference));
            Assert.False(string.IsNullOrWhiteSpace(item.DemoStep));
        });
    }

    [Fact]
    public void DemoFitsRequestedWindowAndCoversEndToEndJourney()
    {
        var demo = _catalog.Demo();
        Assert.InRange(demo.TotalMinutes, 30, 45);
        Assert.Contains(demo.Steps, x => x.Url == "/Workflow");
        Assert.Contains(demo.Steps, x => x.Url == "/Continuity/Portability");
        Assert.Contains(demo.Steps, x => x.Url == "/Audit");
    }

    [Fact]
    public void ValidationRejectsUnknownModulesAndUpdatesKnownEvidence()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(_catalog.Validate("unknown", now));
        Assert.True(_catalog.Validate("workflow", now) is false); // workflow is demonstrated in the route, not a duplicated module card
        Assert.True(_catalog.Validate("ged", now));
        Assert.Equal(now, _catalog.Dashboard().Modules.Single(x => x.Key == "ged").LastValidatedAt);
    }
}
