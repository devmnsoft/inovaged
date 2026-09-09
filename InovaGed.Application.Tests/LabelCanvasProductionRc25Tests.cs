using System.Text.Json;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;
using Microsoft.Extensions.Logging.Abstractions;

namespace InovaGed.Application.Tests;

public sealed class LabelCanvasProductionRc25Tests
{
    [Theory]
    [InlineData("Box","BOX")][InlineData("LocDeskBox","BOX")][InlineData("Document","DOCUMENT")][InlineData("Folder","DOCUMENT")]
    [InlineData("MedicalRecord","DOCUMENT")][InlineData("Process","DOCUMENT")][InlineData("LocDeskFolder","DOCUMENT")][InlineData("Batch","BATCH")]
    public void Subject_mapper_has_one_operational_contract(string semantic,string expected)=>Assert.Equal(expected,LabelCanvasSubjectTypeMapper.ToOperational(semantic));

    [Fact]
    public void Schema_one_is_read_and_group_id_round_trips_as_schema_two()
    {
        const string legacy="""{"schemaVersion":1,"canvas":{"widthMm":100,"heightMm":70},"elements":[{"id":"a","type":"text","name":"Título","xMm":3,"yMm":3,"widthMm":20,"heightMm":8,"groupId":"g1","text":"Teste"}],"bindings":{"subjectType":"Document"}}""";
        var document=JsonSerializer.Deserialize<LabelCanvasDocumentDto>(legacy,new JsonSerializerOptions(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true})!;
        Assert.Equal(1,document.SchemaVersion);Assert.Equal("g1",document.Elements[0].GroupId);document.SchemaVersion=2;
        var roundTrip=JsonSerializer.Serialize(document,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"schemaVersion\":2",roundTrip);Assert.Contains("\"groupId\":\"g1\"",roundTrip);
    }

    [Fact]
    public void Runtime_required_field_is_an_error_and_optional_empty_is_allowed()
    {
        var renderer=Renderer();var design=Design("""{"schemaVersion":2,"canvas":{"widthMm":100,"heightMm":70,"safeMarginMm":0},"elements":[{"id":"required","type":"field","name":"Código","xMm":1,"yMm":1,"widthMm":30,"heightMm":8,"binding":{"field":"documentCode"},"validation":{"required":true}},{"id":"optional","type":"field","name":"Observação","xMm":1,"yMm":12,"widthMm":30,"heightMm":8,"binding":{"field":"notes"},"validation":{"required":false}}],"bindings":{"subjectType":"Document"}}""");
        var result=renderer.Render(design,new Dictionary<string,object?>{{"documentCode",null},{"notes",null}});
        Assert.Contains(result.Validation.Issues,x=>x.Code=="REQUIRED_VALUE"&&x.ElementId=="required");Assert.DoesNotContain(result.Validation.Issues,x=>x.Code=="REQUIRED_VALUE"&&x.ElementId=="optional");
    }

    [Fact]
    public void Formatters_and_visibility_are_safe_and_deterministic()
    {
        var renderer=Renderer();var design=Design("""{"schemaVersion":2,"canvas":{"widthMm":100,"heightMm":70,"safeMarginMm":0},"elements":[{"id":"upper","type":"field","name":"Código","xMm":1,"yMm":1,"widthMm":40,"heightMm":8,"binding":{"field":"code","prefix":"[","suffix":"]","format":"UPPERCASE"}},{"id":"hidden","type":"field","name":"LED","xMm":1,"yMm":12,"widthMm":40,"heightMm":8,"binding":{"field":"led"},"visibilityCondition":{"operator":"HAS_VALUE","field":"led"}}],"bindings":{"subjectType":"Document"}}""");
        var result=renderer.Render(design,new Dictionary<string,object?>{{"code","doc-9"},{"led",""}});
        Assert.Contains("[DOC-9]",result.Html);Assert.DoesNotContain("data-element-id=\"hidden\"",result.Html);
    }

    [Fact]
    public void Code128_is_vectorial_and_changes_with_the_encoded_value()
    {
        var first=LabelCanvasRenderService.BuildCode128Svg("DOC-001");var second=LabelCanvasRenderService.BuildCode128Svg("DOC-002");
        Assert.Contains("class=\"code128\"",first);Assert.Contains("<rect",first);Assert.DoesNotContain("repeating-linear-gradient",first);Assert.NotEqual(first,second);
    }

    [Theory]
    [InlineData("/Administration/BrandAssets/11111111-2222-3333-4444-555555555555/File",true)]
    [InlineData("data:image/png;base64,iVBORw0KGgo=",true)]
    [InlineData("data:image/png;base64,QQ==",false)]
    [InlineData("/uploads/logo.png",false)][InlineData("https://example.com/logo.png",false)][InlineData("javascript:alert(1)",false)][InlineData("file:///c:/logo.png",false)][InlineData("C:\\logo.png",false)]
    public void Image_source_policy_blocks_external_and_file_paths(string source,bool expected)=>Assert.Equal(expected,LabelCanvasSafeImageSource.IsSafe(source));

    [Fact]
    public void Batch_keeps_values_and_copies_independent()
    {
        var renderer=Renderer();var design=Design("""{"schemaVersion":2,"canvas":{"widthMm":100,"heightMm":70,"safeMarginMm":0},"elements":[{"id":"code","type":"field","name":"Caixa","xMm":1,"yMm":1,"widthMm":40,"heightMm":8,"binding":{"field":"boxCode"}}],"bindings":{"subjectType":"Box"}}""");
        IReadOnlyDictionary<string,object?>[] values=[new Dictionary<string,object?>{{"boxCode","CX-01"},{"__copies",2}},new Dictionary<string,object?>{{"boxCode","CX-02"},{"__copies",2}}];var result=renderer.RenderBatch(design,values);
        Assert.Equal(2,Count(result.Html,"CX-01"));Assert.Equal(2,Count(result.Html,"CX-02"));Assert.Equal(4,Count(result.Html,"class=\"label-canvas-output\""));
    }

    private static LabelCanvasRenderService Renderer()=>new(NullLogger<LabelCanvasRenderService>.Instance);
    private static LabelCanvasDesignDto Design(string json)=>new(){TemplateKey="TEST_CANVAS_V1",TemplateName="Teste",DesignJson=json,SubjectType="Document"};
    private static int Count(string value,string fragment)=>(value.Length-value.Replace(fragment,"",StringComparison.Ordinal).Length)/fragment.Length;
}
