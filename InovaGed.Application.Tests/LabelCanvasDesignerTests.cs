using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;
using Microsoft.Extensions.Logging.Abstractions;

namespace InovaGed.Application.Tests;

public sealed class LabelCanvasDesignerTests
{
    [Fact]
    public void Renderer_uses_millimeters_hash_and_never_emits_invalid_image_source()
    {
        var renderer=new LabelCanvasRenderService(NullLogger<LabelCanvasRenderService>.Instance);
        const string json="""{"schemaVersion":1,"canvas":{"widthMm":100,"heightMm":70,"paper":"A4","orientation":"portrait","gridMm":2,"safeMarginMm":3},"elements":[{"id":"logo","type":"logo","name":"Logo","xMm":5,"yMm":5,"widthMm":20,"heightMm":10,"rotationDeg":0,"zIndex":1,"locked":false,"visible":true,"style":{"fontFamily":"Arial","fontSizePt":9,"fontWeight":"400","align":"center","color":"#111111","backgroundColor":"transparent","border":"none","paddingMm":0},"binding":{"asset":"data:,","fallback":"Marca"},"validation":{}}],"bindings":{"subjectType":"Document","sampleDataProfile":"Documento GED"}}""";
        var result=renderer.Render(new LabelCanvasDesignDto{TemplateKey="TEST_CANVAS_V1",TemplateName="Teste",DesignJson=json},new Dictionary<string,object?>());
        Assert.Contains("width:100mm",result.Html);Assert.Contains("height:70mm",result.Html);Assert.Contains("Marca",result.Html);
        Assert.DoesNotContain("src=\"\"",result.Html);Assert.DoesNotContain("src=\"data:,\"",result.Html);Assert.DoesNotContain("src=\"/data:,\"",result.Html);
        Assert.Equal(64,result.SnapshotHash.Length);
    }

    [Fact]
    public void Hol_catalog_exposes_all_governed_bindings_with_friendly_labels()
    {
        var catalog=new LabelCanvasFieldCatalogService();var fields=catalog.GetFields("LocDeskFolder");
        foreach(var key in new[]{"contractName","controlNumber","volumeNumber","subject","details","activity","classification","support","documentPeriod","currentPhase","eliminationForecast","eliminationStatus","ledNumber","location","traceCode","qrPayload"})
            Assert.Contains(fields,x=>x.Key==key&&!string.IsNullOrWhiteSpace(x.Label));
        var sample=catalog.GetSampleData("HOL");Assert.Equal("ARQUIVO LOCDESCK ANANINDEUA",sample["archiveTitle"]);Assert.Equal("199",sample["controlNumber"]);
    }

    [Fact]
    public void Static_contract_contains_routes_assets_and_initial_templates()
    {
        var root=FindRoot();var controller=File.ReadAllText(Path.Combine(root,"InovaGed.Web","Controller","LabelDesignerController.cs"));var migration=File.ReadAllText(Path.Combine(root,"database","migrations","2026_09_08_label_canvas_designer_rc22.sql"));
        foreach(var route in new[]{"/Labels/Designer/New","/Labels/Designer/Edit/{templateKey}","/Labels/Designer/Preview/{templateKey}","/Labels/Designer/Versions/{templateKey}","/Labels/Designer/Fields"})Assert.Contains(route,controller);
        foreach(var key in new[]{"LOCDESK_PASTA_CANVAS_V1","LOCDESK_CAIXA_CANVAS_V1","HOL_PRONTUARIO_CANVAS_V1","GED_DOCUMENTO_CANVAS_V1","GED_CAIXA_CANVAS_V1"})Assert.Contains(key,migration);
        Assert.True(File.Exists(Path.Combine(root,"InovaGed.Web","wwwroot","css","labels-designer.css")));Assert.True(File.Exists(Path.Combine(root,"InovaGed.Web","wwwroot","js","labels-designer.js")));
    }

    private static string FindRoot(){var current=new DirectoryInfo(AppContext.BaseDirectory);while(current is not null&&!File.Exists(Path.Combine(current.FullName,"InovaGed.sln")))current=current.Parent;return current?.FullName??throw new DirectoryNotFoundException();}
}
