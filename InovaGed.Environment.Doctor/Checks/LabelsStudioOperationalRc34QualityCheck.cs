namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsStudioOperationalRc34QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var controller=File.ReadAllText(Path.Combine(root,"InovaGed.Web","Controller","LabelDesignerController.cs"));
        var client=File.ReadAllText(Path.Combine(root,"InovaGed.Web","wwwroot","js","labels-designer.js"));
        var dialogs=File.ReadAllText(Path.Combine(root,"InovaGed.Web","Views","Labels","Designer","_DesignerDialogs.cshtml"));
        var migration=File.ReadAllText(Path.Combine(root,"database","migrations","2026_09_14_rc34_operational_closure.sql"));
        var checks=new Dictionary<string,bool>{
            ["conflict dialog real"]=dialogs.Contains("data-conflict-dialog")&&client.Contains("showConflictDialog"),
            ["no force overwrite"]=!dialogs.Contains("forçar salvar",StringComparison.OrdinalIgnoreCase),
            ["conflito compara local e servidor"]=controller.Contains("CompareConflict")&&client.Contains("showDiff"),
            ["conflito duplica draft local"]=controller.Contains("DuplicateConflict"),
            ["component presets possuem schema tenant"]=migration.Contains("label_canvas_component_preset")&&migration.Contains("tenant_id"),
            ["diff UI visível"]=dialogs.Contains("data-diff-drawer"),
            ["no alert de conflict"]=!client.Contains("alert("),
            ["no prompt de layer rename"]=!client.Contains("prompt("),
            ["dimension safety preservada"]=client.Contains("width<20||width>500")};
        foreach(var x in checks)output.WriteLine($"[{(x.Value?"OK":"FALHA")}] {x.Key}");
        if(checks.Values.All(x=>x))return 0;error.WriteLine("[FALHA] Label Studio operacional RC34 incompleto.");return 2;
    }
}
