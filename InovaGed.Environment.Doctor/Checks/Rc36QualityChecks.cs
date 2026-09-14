namespace InovaGed.Environment.Doctor.Checks;

public static class Rc36QualityChecks
{
    public static int LabelSchema(string root, TextWriter output, TextWriter error) => Run(output,error,new Dictionary<string,bool>
    {
        ["RC33 no catálogo"] = Read(root,"database","required_migrations.json").Contains("2026_09_11_rc33_collaboration_and_intake"),
        ["RC34 no catálogo"] = Read(root,"database","required_migrations.json").Contains("2026_09_14_rc34_operational_closure"),
        ["migration lock_version idempotente"] = Read(root,"database","migrations","2026_09_11_rc33_collaboration_and_intake.sql").Contains("add column if not exists lock_version",StringComparison.OrdinalIgnoreCase),
        ["schema capability"] = File.Exists(Path.Combine(root,"InovaGed.Infrastructure","Labels","LabelCanvasSchemaCapabilities.cs")),
        ["leitura compatível"] = Read(root,"InovaGed.Infrastructure","Labels","LabelCanvasDesignRepository.cs").Contains("1::bigint as LockVersion"),
        ["escrita protegida"] = Read(root,"InovaGed.Infrastructure","Labels","LabelCanvasDesignRepository.cs").Contains("RequireWritableSchemaAsync")
    });
    public static int LabelsExperience(string root,TextWriter output,TextWriter error)=>Run(output,error,new Dictionary<string,bool>
    {
        ["PrintWizard stepper"] = Read(root,"InovaGed.Web","Views","Labels","PrintWizard.cshtml").Contains("label-stepper"),
        ["QuickPreview resiliente"] = Read(root,"InovaGed.Web","wwwroot","js","labels-printwizard.js").Contains("previewSchemaBlocked"),
        ["proporção real"] = Read(root,"InovaGed.Web","Views","Labels","Designer","Index.cshtml").Contains("aspect-ratio"),
        ["ícones válidos"] = !Read(root,"InovaGed.Web","Views","Labels","Shared","_LabelHelpDrawer.cshtml").Contains("name=\"x\"")
    });
    public static int DatabaseReadinessUx(string root,TextWriter output,TextWriter error)=>Run(output,error,new Dictionary<string,bool>
    {
        ["catálogo completo"] = Read(root,"database","required_migrations.json").Contains("2026_09_14_rc34_operational_closure"),
        ["pendências visíveis"] = Read(root,"InovaGed.Web","Views","DatabaseReadiness","Plan.cshtml").Contains("Pendente"),
        ["apply protegido"] = Read(root,"InovaGed.Web","Controller","DatabaseReadinessController.cs").Contains("AppPolicies.SchemaRepair"),
        ["resultado por migration"] = Read(root,"InovaGed.Web","Views","DatabaseReadiness","Plan.cshtml").Contains("Falha")
    });
    private static string Read(string root,params string[] parts)=>File.ReadAllText(Path.Combine(new[]{root}.Concat(parts).ToArray()));
    private static int Run(TextWriter output,TextWriter error,IReadOnlyDictionary<string,bool> checks){foreach(var x in checks)output.WriteLine($"[{(x.Value?"OK":"FALHA")}] {x.Key}");if(checks.Values.All(x=>x))return 0;error.WriteLine("[FALHA] Contrato RC36 incompleto.");return 2;}
}
