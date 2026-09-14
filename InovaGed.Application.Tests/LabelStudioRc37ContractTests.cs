namespace InovaGed.Application.Tests;

public sealed class LabelStudioRc37ContractTests
{
    private static string Read(params string[] parts)=>File.ReadAllText(GlobalJsonContractTests.Root(Path.Combine(parts)));
    [Fact] public void preview_subject_search_is_tenant_scoped_and_respects_acl(){var s=Read("InovaGed.Web","Controller","LabelDesignerController.cs");Assert.Contains("tenant_id=@tenantId",s);Assert.Contains("CanAccessGedAsync",s);Assert.Contains("PreviewSubjectAccessibleAsync",s);}
    [Fact] public void preview_subject_accepts_unsaved_design_without_trace_or_history(){var s=Read("InovaGed.Web","Controller","LabelDesignerController.cs");var a=s.IndexOf("PreviewSubject(string",StringComparison.Ordinal);var b=s.IndexOf("StarterPreview(",a,StringComparison.Ordinal);var section=s[a..b];Assert.Contains("request.DesignJson",section);Assert.DoesNotContain("RecordEventAsync",section);Assert.DoesNotContain("Print",section,StringComparison.OrdinalIgnoreCase);}
    [Fact] public void starter_preview_is_readonly_and_uses_dimension_policy(){var s=Read("InovaGed.Web","Controller","LabelDesignerController.cs");var a=s.IndexOf("StarterPreview(",StringComparison.Ordinal);var b=s.IndexOf("Publish(",a,StringComparison.Ordinal);var section=s[a..b];Assert.Contains("LabelCanvasDimensionPolicy",section);Assert.Contains("starters.Create",section);Assert.DoesNotContain("CreateDraftAsync",section);Assert.DoesNotContain("RecordEventAsync",section);}
    [Fact] public void designer_has_real_preview_selector_and_cancelable_search(){var v=Read("InovaGed.Web","Views","Labels","Designer","_DesignerToolbar.cshtml");var js=Read("InovaGed.Web","wwwroot","js","labels-designer.js");Assert.Contains("Dados da prévia",v);Assert.Contains("Registro real",v);Assert.Contains("AbortController",js);Assert.Contains("setTimeout(searchPreviewSubjects,400)",js);}
    [Fact] public void starter_wizard_has_renderer_preview(){Assert.Contains("data-starter-preview",Read("InovaGed.Web","Views","Labels","Designer","New.cshtml"));Assert.Contains("/Labels/Designer/StarterPreview",Read("InovaGed.Web","wwwroot","js","labels-new-wizard.js"));}
}
