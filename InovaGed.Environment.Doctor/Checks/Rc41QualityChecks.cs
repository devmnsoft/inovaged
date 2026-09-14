namespace InovaGed.Environment.Doctor.Checks;

public static class Rc41QualityChecks
{
    public static int SmartPreflight(string root,TextWriter output,TextWriter error)=>Run("Smart preflight",output,error,new Dictionary<string, bool>()
    {
        ["ILabelPreflightService"] = Read(root,"InovaGed.Application/Labels/Intelligence/LabelOperationalIntelligenceContracts.cs").Contains("ILabelPreflightService"),
        ["real subject values"] = Read(root,"InovaGed.Infrastructure/Labels/LabelOperationalIntelligenceService.cs").Contains("ResolveAsync"),
        ["severity and no side effect"] = Read(root,"InovaGed.Application/Labels/Intelligence/LabelOperationalIntelligenceContracts.cs").Contains("CanPrint") && !Read(root,"InovaGed.Infrastructure/Labels/LabelOperationalIntelligenceService.cs").Contains("CreateJob"),
        ["wizard and batch"] = Read(root,"InovaGed.Web/Views/Labels/_PrintWizardFinalReview.cshtml").Contains("data-label-preflight") && Read(root,"InovaGed.Application/Labels/Intelligence/LabelOperationalIntelligenceContracts.cs").Contains("CheckBatchAsync")
    });
    public static int Recommendation(string root,TextWriter output,TextWriter error)=>Run("Template recommendation",output,error,new Dictionary<string, bool>()
    {
        ["deterministic documented weights"] = Read(root,"InovaGed.Infrastructure/Labels/LabelOperationalIntelligenceService.cs").Contains("Documented deterministic weights"),
        ["reasons"] = Read(root,"InovaGed.Application/Labels/Intelligence/LabelOperationalIntelligenceContracts.cs").Contains("Reasons"),
        ["tenant required"] = Read(root,"InovaGed.Infrastructure/Labels/LabelOperationalIntelligenceService.cs").Contains("Tenant obrigatório"),
        ["compatibility filtering"] = Read(root,"InovaGed.Infrastructure/Labels/LabelOperationalIntelligenceService.cs").Contains("compatible=subject")
    });
    public static int DesignerAssist(string root,TextWriter output,TextWriter error)=>Run("Designer assist",output,error,new Dictionary<string, bool>()
    {
        ["auto layout and undo"] = Read(root,"InovaGed.Web/wwwroot/js/labels-designer.js").Contains("data-assist-layout") && Read(root,"InovaGed.Web/wwwroot/js/labels-designer.js").Contains("Use Desfazer"),
        ["locked elements"] = Read(root,"InovaGed.Web/wwwroot/js/labels-designer.js").Contains("filter(e=>!e.locked)"),
        ["overflow"] = Read(root,"InovaGed.Infrastructure/Labels/LabelCanvasRenderService.cs").Contains("TEXT_OVERFLOW"),
        ["conditional UI"] = Read(root,"InovaGed.Web/Views/Labels/Designer/_DesignerInspector.cshtml").Contains("visibilityCondition.operator"),
        ["quick insert and command palette"] = Read(root,"InovaGed.Web/wwwroot/js/labels-designer.js").Contains("openCommandPalette('Adicionar')") && Read(root,"InovaGed.Web/Views/Labels/Designer/_DesignerDialogs.cshtml").Contains("data-command-palette")
    });
    public static int PublishQuality(string root,TextWriter output,TextWriter error)=>Run("Publish quality",output,error,new Dictionary<string, bool>()
    {
        ["validation guard"] = Read(root,"InovaGed.Web/wwwroot/js/labels-designer.js").Contains("Corrija os erros críticos"),
        ["compatibility"] = Read(root,"InovaGed.Web/Views/Labels/Designer/_DesignerToolbar.cshtml").Contains("Compatibilidade"),
        ["real data testing"] = Read(root,"InovaGed.Web/Views/Labels/Designer/_DesignerToolbar.cshtml").Contains("Testar modelo"),
        ["immutable restore"] = Read(root,"InovaGed.Web/wwwroot/js/labels-designer.js").Contains("copiada para uma revisão")
    });
    private static string Read(string root,string path)=>File.ReadAllText(Path.Combine(root,path));
    private static int Run(string name,TextWriter output,TextWriter error,IReadOnlyDictionary<string,bool> checks){foreach(var x in checks)output.WriteLine($"[{(x.Value?"OK":"FALHA")}] {x.Key}");if(checks.Values.All(x=>x))return 0;error.WriteLine($"[FALHA] {name} incompleto.");return 2;}
}
