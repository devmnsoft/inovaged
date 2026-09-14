namespace InovaGed.Environment.Doctor.Checks;

public static class Rc39QualityChecks
{
    public static int PrintStateMachine(string root,TextWriter output,TextWriter error) => Run(output,error,"Governança de impressão RC39",new Dictionary<string,bool>
    {
        ["state machine central"] = Read(root,"InovaGed.Application/Labels/Printing/LabelPrintStateMachine.cs").Contains("EnsureTransition"),
        ["estados terminais"] = Read(root,"InovaGed.Application/Labels/Printing/LabelPrintStateMachine.cs").Contains("Printed or LabelPrintJobStatus.Cancelled"),
        ["idempotência concorrente"] = Read(root,"InovaGed.Infrastructure/PhysicalArchive/LabelPrintJobService.cs").Contains("on conflict (tenant_id,requested_by,client_action_id)"),
        ["artifact governance"] = Read(root,"InovaGed.Infrastructure/PhysicalArchive/LabelPrintJobService.cs").Contains("artifact_sha256") && Read(root,"database/migrations/2026_09_14_label_print_governance_rc39.sql").Contains("artifact_bytes"),
        ["reprint governance"] = Read(root,"InovaGed.Infrastructure/PhysicalArchive/LabelPrintJobService.cs").Contains("RequireReprintReason")
    });

    public static int PrintOperationsUx(string root,TextWriter output,TextWriter error) => Run(output,error,"Operações UX RC39",new Dictionary<string,bool>
    {
        ["status humanos"] = Read(root,"InovaGed.Application/Labels/Printing/LabelPrintContracts.cs").Contains("Aguardando preparação"),
        ["paginação"] = Read(root,"InovaGed.Infrastructure/PhysicalArchive/LabelPrintJobService.cs").Contains("offset @Offset"),
        ["KPI real"] = Read(root,"InovaGed.Infrastructure/PhysicalArchive/LabelPrintJobService.cs").Contains("GetMetricsAsync"),
        ["PrintJob timeline"] = Read(root,"InovaGed.Web/Views/Labels/PrintJobDetails.cshtml").Contains("Linha do tempo"),
        ["calibration UX"] = Read(root,"InovaGed.Web/Views/Labels/Calibration.cshtml").Contains("Centro de Calibração")
    });

    public static int TraceabilityUx(string root,TextWriter output,TextWriter error) => Run(output,error,"Rastreabilidade UX RC39",new Dictionary<string,bool>
    {
        ["scanner operacional"] = Read(root,"InovaGed.Web/Views/Labels/Scanner.cshtml").Contains("URL pública, token ou TraceCode"),
        ["trace timeline"] = Read(root,"InovaGed.Web/Views/Labels/Trace.cshtml").Contains("timeline",StringComparison.OrdinalIgnoreCase),
        ["replacement"] = Read(root,"InovaGed.Web/Views/Labels/Replacements.cshtml").Contains("substitu",StringComparison.OrdinalIgnoreCase),
        ["exposição pública mínima"] = !Read(root,"InovaGed.Web/Views/Labels/TracePublic.cshtml").Contains("TenantId",StringComparison.OrdinalIgnoreCase)
    });

    private static string Read(string root,string relative)=>File.ReadAllText(Path.Combine(root,relative));
    private static int Run(TextWriter output,TextWriter error,string name,IReadOnlyDictionary<string,bool> checks)
    {
        foreach(var check in checks)output.WriteLine($"[{(check.Value?"OK":"FALHA")}] {check.Key}");
        if(checks.Values.All(value=>value))return 0;
        error.WriteLine($"[FALHA] {name} incompleto.");return 2;
    }
}
