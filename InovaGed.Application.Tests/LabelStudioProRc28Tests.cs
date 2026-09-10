using System.Text.Json;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;

namespace InovaGed.Application.Tests;

public sealed class LabelStudioProRc28Tests
{
    private readonly LabelCanvasFieldCatalogService catalog=new();
    [Fact] public void starter_blank_is_neutral(){var doc=Starter().Create(new("Box",LabelCanvasStarterKind.Blank,100,70,null));Assert.Empty(doc.Elements);Assert.Equal("GENERIC",doc.Bindings.SampleDataProfile);}
    [Fact] public void starter_traceability_has_no_hol(){var json=JsonSerializer.Serialize(Starter().Create(new("MedicalRecord",LabelCanvasStarterKind.Traceability,100,70,null)));Assert.DoesNotContain("HOL",json,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("LocDesk",json,StringComparison.OrdinalIgnoreCase);}
    [Fact] public void starter_only_uses_supported_fields(){var doc=Starter().Create(new("Box",LabelCanvasStarterKind.Institutional,100,70,null));var allowed=catalog.GetFields("Box").Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);Assert.All(doc.Elements.Where(x=>x.Binding?.Field is not null),x=>Assert.Contains(x.Binding!.Field!,allowed));}
    [Fact] public void manual_label_rejects_unknown_field(){var result=Manual().Validate(Design(required:false),new Dictionary<string,string?>{{"javascript:payload","x"}});Assert.Contains(result.Issues,x=>x.Code=="MANUAL_UNKNOWN_FIELD");}
    [Fact] public void manual_label_required_fields_are_enforced(){var result=Manual().Validate(Design(required:true),new Dictionary<string,string?>());Assert.Contains(result.Issues,x=>x.Code=="MANUAL_REQUIRED");}
    [Fact] public void paper_options_are_consistent(){Assert.Equal(new[]{"A4","A5","LETTER","CUSTOM"},LabelPaperOptions.Supported);}
    [Fact] public void quick_preview_does_not_register_history(){var source=File.ReadAllText(Path.Combine(Root(),"InovaGed.Web","Controller","LabelsController.cs"));var method=source[source.IndexOf("Task<IActionResult> QuickPreview",StringComparison.Ordinal)..source.IndexOf("[HttpPost, ValidateAntiForgeryToken]",source.IndexOf("Task<IActionResult> QuickPreview",StringComparison.Ordinal),StringComparison.Ordinal)];Assert.Contains("BuildLabelRenderModelAsync(input,false",method);Assert.DoesNotContain("_printRegistrar.RegisterAsync",method);}
    [Fact] public void quick_preview_does_not_issue_trace()=>quick_preview_does_not_register_history();
    [Fact] public void canvas_logo_geometry_is_not_overridden_by_irrelevant_wizard_fields(){var source=File.ReadAllText(Path.Combine(Root(),"InovaGed.Web","Controller","LabelsController.cs"));var start=source.IndexOf("RenderCanvasTemplateAsync",StringComparison.Ordinal);Assert.DoesNotContain("LogoWidthMm",source[start..source.IndexOf("LoadSubjectOptionsAsync",start,StringComparison.Ordinal)]);}
    [Fact] public void generated_template_key_is_unique(){var source=File.ReadAllText(Path.Combine(Root(),"InovaGed.Web","Controller","LabelDesignerController.cs"));Assert.Contains("UniqueKeyAsync",source);Assert.Contains("suffix=2",source);}
    [Fact] public void manual_label_is_tenant_scoped(){var source=File.ReadAllText(Path.Combine(Root(),"InovaGed.Infrastructure","Labels","ManualLabelInstanceService.cs"));Assert.Contains("tenant_id=@tenantId",source);}
    [Fact] public void manual_label_print_uses_manual_instance_id(){var source=File.ReadAllText(Path.Combine(Root(),"InovaGed.Web","Controller","LabelsController.cs"));Assert.Contains("input.SubjectId=manualInstance.Id",source);}
    private LabelCanvasStarterTemplateService Starter()=>new(catalog);private ManualLabelInstanceService Manual()=>new(null!,catalog);
    private static LabelCanvasDesignDto Design(bool required){var doc=new LabelCanvasDocumentDto{Bindings=new(){SubjectType="Document"},Elements=[new(){Id="title",Name="Título",Type="field",Binding=new(){Field="documentTitle"},Validation=new(){Required=required,MaxCharacters=20}}]};return new(){TemplateKey="MANUAL",TemplateName="Manual",SubjectType="Document",DesignJson=JsonSerializer.Serialize(doc)};}
    private static string Root(){var current=new DirectoryInfo(AppContext.BaseDirectory);while(current is not null&&!File.Exists(Path.Combine(current.FullName,"InovaGed.sln")))current=current.Parent;return current?.FullName??throw new DirectoryNotFoundException();}
}
