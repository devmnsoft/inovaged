using System.Reflection;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;
using InovaGed.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Application.Tests;

public sealed class LabelDesignerSaveContractTests
{
    [Fact]
    public void Save_accepts_only_json_and_nullable_body_is_guarded_before_mapping()
    {
        var action=typeof(LabelDesignerController).GetMethod(nameof(LabelDesignerController.Save))!;
        var consumes=Assert.Single(action.GetCustomAttributes<ConsumesAttribute>());
        Assert.Contains("application/json",consumes.ContentTypes);
        Assert.True(new NullabilityInfoContext().Create(action.GetParameters()[1]).ReadState is NullabilityState.Nullable);

        var source=File.ReadAllText(Path.Combine(FindRoot(),"InovaGed.Web","Controller","LabelDesignerController.cs"));
        var guard=source.IndexOf("if(request is null)return InvalidSaveRequest",StringComparison.Ordinal);
        var mapping=source.IndexOf("request=CopyWithKey(request,templateKey)",StringComparison.Ordinal);
        Assert.True(guard>=0&&mapping>guard,"O corpo nulo deve ser rejeitado antes do mapeamento.");
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"canvas\":null,\"elements\":[],\"bindings\":{}}","MISSING_CANVAS")]
    [InlineData("{\"schemaVersion\":2,\"canvas\":{},\"elements\":null,\"bindings\":{}}","MISSING_ELEMENTS")]
    [InlineData("{\"schemaVersion\":2,\"canvas\":{},\"elements\":[null],\"bindings\":{}}","NULL_ELEMENT")]
    [InlineData("{\"schemaVersion\":2,\"canvas\":{},\"elements\":[],\"bindings\":null}","MISSING_BINDINGS")]
    public void Required_internal_design_structure_is_rejected_before_rendering(string designJson,string expectedCode)
    {
        var method=typeof(LabelCanvasDesignRepository).GetMethod("ValidateRequest",BindingFlags.NonPublic|BindingFlags.Static)!;
        var request=new LabelCanvasSaveRequest{TemplateKey="VALID_KEY",TemplateName="Modelo",SubjectType="Document",PaperKind="A4",WidthMm=100,HeightMm=70,Orientation="portrait",DesignJson=designJson};
        var invocation=Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,[request]));
        var error=Assert.IsType<LabelCanvasRequestException>(invocation.InnerException);
        Assert.Equal(expectedCode,error.Code);
    }

    [Fact]
    public void Empty_element_list_is_a_valid_draft_structure()
    {
        var method=typeof(LabelCanvasDesignRepository).GetMethod("ValidateRequest",BindingFlags.NonPublic|BindingFlags.Static)!;
        const string json="""{"schemaVersion":2,"canvas":{"widthMm":100,"heightMm":70},"elements":[],"bindings":{"subjectType":"Document"}}""";
        var request=new LabelCanvasSaveRequest{TemplateKey="VALID_KEY",TemplateName="Modelo",SubjectType="Document",PaperKind="A4",WidthMm=100,HeightMm=70,Orientation="portrait",DesignJson=json};
        method.Invoke(null,[request]);
    }

    [Fact]
    public void Browser_keeps_new_edits_pending_and_does_not_persist_design_in_web_storage()
    {
        var script=File.ReadAllText(Path.Combine(FindRoot(),"InovaGed.Web","wwwroot","js","labels-designer.js"));
        Assert.Contains("const sentRevision=editRevision",script);
        Assert.Contains("dirty=editRevision!==sentRevision",script);
        Assert.DoesNotContain("localStorage.setItem(localKey",script);
        Assert.DoesNotContain("localStorage.getItem(localKey",script);
    }

    private static string FindRoot(){var current=new DirectoryInfo(AppContext.BaseDirectory);while(current is not null&&!File.Exists(Path.Combine(current.FullName,"InovaGed.sln")))current=current.Parent;return current?.FullName??throw new DirectoryNotFoundException();}
}
