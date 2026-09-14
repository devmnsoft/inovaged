namespace InovaGed.Environment.Doctor.Checks;

public static class Rc40QualityChecks
{
    public static int Operations(string root, TextWriter output, TextWriter error) => Run(output,error,"Workspace operacional",new Dictionary<string,bool>()
    {
        ["hub reorganizado"] = Read(root,"InovaGed.Web/Views/Labels/Index.cshtml").Contains("Criar e imprimir") && Read(root,"InovaGed.Web/Views/Labels/Index.cshtml").Contains("Acompanhar"),
        ["queue actions via state machine"] = Read(root,"InovaGed.Application/Labels/Printing/LabelPrintContracts.cs").Contains("LabelPrintJobPresentation"),
        ["job timeline"] = Read(root,"InovaGed.Web/Views/Labels/PrintJobDetails.cshtml").Contains("Andamento real"),
        ["no native confirm"] = !Labels(root).Contains("confirm("),
        ["reprint dialog"] = Read(root,"InovaGed.Web/Views/Labels/PrintJobDetails.cshtml").Contains("id=\"reprint\""),
        ["artifact integrity"] = Read(root,"InovaGed.Infrastructure/PhysicalArchive/LabelPrintJobService.cs").Contains("VerifyArtifactAsync")
    });
    public static int Calibration(string root, TextWriter output, TextWriter error) => Run(output,error,"Calibration Center",new Dictionary<string,bool>()
    {
        ["calibration wizard"] = Read(root,"InovaGed.Web/Views/Labels/Calibration.cshtml").Contains("Assistente de calibração"),
        ["test pattern"] = Read(root,"InovaGed.Web/Views/Labels/Calibration.cshtml").Contains("50 × 50 mm"),
        ["no trace"] = !Read(root,"InovaGed.Web/Controller/LabelsController.cs").Contains("CalibrationPrintTest") || !Read(root,"InovaGed.Web/Controller/LabelsController.cs").Split("CalibrationPrintTest")[1].Split('\n')[0].Contains("Trace"),
        ["range validation"] = Read(root,"InovaGed.Web/wwwroot/js/labels-calibration-rc40.js").Contains("suggested >= 80"),
        ["pt-BR"] = Read(root,"InovaGed.Web/wwwroot/js/labels-calibration-rc40.js").Contains("replace(',', '.')")
    });
    public static int Traceability(string root, TextWriter output, TextWriter error) => Run(output,error,"Traceability Center",new Dictionary<string,bool>()
    {
        ["trace search"] = Read(root,"InovaGed.Web/Views/Labels/Index.cshtml").Contains("Rastreabilidade"),
        ["identity status"] = Read(root,"InovaGed.Web/Views/Labels/Trace.cshtml").Contains("Status"),
        ["timeline"] = Read(root,"InovaGed.Web/Views/Labels/Trace.cshtml").Contains("timeline",StringComparison.OrdinalIgnoreCase),
        ["scanner"] = Read(root,"InovaGed.Web/Views/Labels/Scanner.cshtml").Contains("autofocus"),
        ["replacement link"] = File.Exists(Path.Combine(root,"InovaGed.Web/Views/Labels/Replacements.cshtml"))
    });
    public static int Batch(string root, TextWriter output, TextWriter error) => Run(output,error,"Batch resilience",new Dictionary<string,bool>()
    {
        ["sample preview"] = Read(root,"InovaGed.Web/Views/Labels/BatchPrint.cshtml").Contains("Até 5"),
        ["partial failures visible"] = Read(root,"InovaGed.Web/Views/Labels/BatchPrint.cshtml").Contains("data-batch-message"),
        ["no native alert"] = !Read(root,"InovaGed.Web/wwwroot/js/labels-batch.js").Contains("alert("),
        ["no duplicate print"] = Read(root,"InovaGed.Infrastructure/PhysicalArchive/LabelPrintJobService.cs").Contains("item.status!=LabelPrintJobStatus.Printed")
    });
    private static string Labels(string root)=>string.Join('\n',Directory.EnumerateFiles(Path.Combine(root,"InovaGed.Web"),"labels-*.*",SearchOption.AllDirectories).Select(File.ReadAllText).Concat(Directory.EnumerateFiles(Path.Combine(root,"InovaGed.Web/Views/Labels"),"*.cshtml",SearchOption.AllDirectories).Select(File.ReadAllText)));
    private static string Read(string root,string relative)=>File.ReadAllText(Path.Combine(root,relative));
    private static int Run(TextWriter output,TextWriter error,string name,IReadOnlyDictionary<string,bool> checks){foreach(var x in checks)output.WriteLine($"[{(x.Value?"OK":"FALHA")}] {x.Key}");if(checks.Values.All(x=>x))return 0;error.WriteLine($"[FALHA] {name} incompleto.");return 2;}
}
