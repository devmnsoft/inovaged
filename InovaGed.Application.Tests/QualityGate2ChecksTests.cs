using InovaGed.Environment.Doctor.Checks;
using InovaGed.Environment.Doctor.Quality;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class QualityGate2ChecksTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"qg2-{Guid.NewGuid():N}");
    public QualityGate2ChecksTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task RazorSafetyRejectsHistoricalRegressions()
    {
        var views = Directory.CreateDirectory(Path.Combine(root, "InovaGed.Web", "Views"));
        await File.WriteAllTextAsync(Path.Combine(views.FullName, "Bad.cshtml"), "<partial view-data='new ViewDataDictionary(ViewData) { { \"Title\", \"X\" } }' />\n@foreach (var x in new[] { (1, 2) }) {}\n<style>@media print {}</style>\n<button class=\"btn\">X</button>");
        var result = await new RazorQualityCheck().RunAsync(Context(), default);
        Assert.Contains(result, x => x.Status == QualityStatus.Fail && x.Message.Contains("Não usar Title"));
        Assert.Contains(result, x => x.Message.Contains("lista tipada"));
        Assert.Contains(result, x => x.Message.Contains("@media"));
        Assert.Contains(result, x => x.Message.Contains("type explícito"));
    }

    [Fact]
    public async Task DapperSafetyRejectsPublicRecordMaterialization()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "Repository.cs"), "public record PublicRow(System.DateTimeOffset At, System.Guid? Id); class R { void M() => db.QueryAsync<PublicRow>(sql); }");
        var result = await new DapperMappingQualityCheck().RunAsync(Context(), default);
        Assert.Contains(result, x => x.Status == QualityStatus.Fail && x.Message.Contains("PublicRow"));
    }

    [Fact]
    public async Task SecurityScanReadsAntiforgeryPastRouteTemplateBraces()
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, "InovaGed.Web", "Controller"));
        await File.WriteAllTextAsync(Path.Combine(dir.FullName, "SampleController.cs"), """
[Authorize]
public class SampleController {
 [HttpPost, ValidateAntiForgeryToken]
 public void Combined() {}
 [HttpPost("/Labels/Designer/Edit/{templateKey}")]
 [HttpPost("/Labels/Designer/{templateKey}/Save")]
 [ValidateAntiForgeryToken]
 public void Save() {}
 [HttpPost("/open")]
 public void Open() {}
}
""");
        var result = await new SecurityQualityCheck().RunAsync(Context(), default);
        var posts = result.Where(x => x.Status == QualityStatus.Fail && x.Message.Contains("POST sem")).ToArray();
        Assert.Single(posts);
        Assert.Contains("SampleController", posts[0].Message);
    }

    [Fact]
    public async Task DependencyInjectionFindsServiceRegisteredOutsideProgram()
    {
        Directory.CreateDirectory(Path.Combine(root, "InovaGed.Web"));
        await File.WriteAllTextAsync(Path.Combine(root, "InovaGed.Web", "Program.cs"), "public class Program {}\npublic class SampleController { public SampleController(object service) {} }");
        Directory.CreateDirectory(Path.Combine(root, "InovaGed.Infrastructure"));
        await File.WriteAllTextAsync(Path.Combine(root, "InovaGed.Infrastructure", "DependencyInjection.cs"),
            "IPcdVersionResolver IRetentionDestinationRepository IRetentionAuditWriter ILabelTemplateCatalogService ILabelPrintJobService ILabelPdfRenderService ILabelPrintRegistrar ILabelPayloadBuilder ILabelQrCodeService InstrumentVersionRepository");
        var result = await new DependencyInjectionQualityCheck().RunAsync(Context(), default);
        Assert.DoesNotContain(result, x => x.Status == QualityStatus.Fail);
    }

    [Fact]
    public async Task IncidentCheckWarnsWhenDatabaseIsNotRequired()
    {
        var result = await new IncidentCenterQualityCheck().RunAsync(new QualityContext(root, true, "Host=127.0.0.1;Port=1;Database=inovaged_quality_gate_offline;Username=none", null), default);
        Assert.Contains(result, x => x.Status == QualityStatus.Warning);
        Assert.DoesNotContain(result, x => x.Status == QualityStatus.Fail);
    }

    [Fact]
    public async Task PerformanceCheckReportsSelectStar()
    {
        var infra = Directory.CreateDirectory(Path.Combine(root, "InovaGed.Infrastructure"));
        await File.WriteAllTextAsync(Path.Combine(infra.FullName, "Query.cs"), "const string Sql = \"select * from ged.documents\";");
        var result = await new PerformanceQualityCheck().RunAsync(Context(), default);
        Assert.Contains(result, x => x.Status == QualityStatus.Warning && x.Message.Contains("SELECT *"));
    }

    private QualityContext Context() => new(root, true, null, null);
    public void Dispose() => Directory.Delete(root, true);
}
