namespace InovaGed.Application.Tests;

public sealed class LabelReusePortabilityRc38ContractTests
{
    private static readonly string Root=FindRoot();
    [Fact] public void Component_preset_operations_are_tenant_scoped_and_soft_archived(){var source=Read("InovaGed.Infrastructure/Labels/LabelCanvasComponentPresetService.cs");Assert.Contains("tenant_id=@tenantId",source);Assert.Contains("reg_status='I'",source);Assert.DoesNotContain("delete from ged.label_canvas_component_preset",source,StringComparison.OrdinalIgnoreCase);}
    [Fact] public void Component_preset_does_not_store_runtime_values_or_ids(){var source=Read("InovaGed.Infrastructure/Labels/LabelCanvasComponentPresetService.cs");Assert.Contains("resolvedValue",source);Assert.Contains("element.Remove(\"id\")",source);Assert.Contains("UNSAFE_ASSET",source);}
    [Fact] public void Template_import_always_creates_a_new_draft_key(){var source=Read("InovaGed.Web/Controller/LabelDesignerController.cs");var section=source[source.IndexOf("IActionResult> Import",StringComparison.Ordinal)..source.IndexOf("ComponentPresets(CancellationToken",StringComparison.Ordinal)];Assert.Contains("UniqueKeyAsync",section);Assert.Contains("CreateDraftAsync",section);Assert.DoesNotContain("SaveDraftAsync",section);}
    [Fact] public void Template_package_rejects_external_urls_and_future_schema(){var source=Read("InovaGed.Infrastructure/Labels/LabelTemplatePackageService.cs");Assert.Contains("uri.Scheme is \"http\" or \"https\" or \"file\" or \"javascript\"",source);Assert.Contains("package.SchemaVersion is <1 or >2",source);}
    [Fact] public void Template_export_contract_has_no_tenant_branding_or_lock_fields(){var contract=Read("InovaGed.Application/Labels/Canvas/LabelCanvasContracts.cs");var start=contract.IndexOf("record LabelTemplatePackageTemplate",StringComparison.Ordinal);var package=contract[start..contract.IndexOf("public interface ILabelTemplatePackageService",start,StringComparison.Ordinal)];Assert.DoesNotContain("TenantId",package);Assert.DoesNotContain("BrandingProfileId",package);Assert.DoesNotContain("LockVersion",package);}
    private static string Read(string relative)=>File.ReadAllText(Path.Combine(Root,relative));
    private static string FindRoot(){var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"InovaGed.sln")))directory=directory.Parent;return directory?.FullName??throw new DirectoryNotFoundException();}
}
