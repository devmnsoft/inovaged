namespace InovaGed.Environment.Doctor.Checks;

public static class Rc37QualityChecks
{
    public static int LabelsStudio(string root,TextWriter output,TextWriter error)
    {
        var controller=Read(root,"InovaGed.Web","Controller","LabelDesignerController.cs");
        var client=Read(root,"InovaGed.Web","wwwroot","js","labels-designer.js");
        var library=Read(root,"InovaGed.Web","Views","Labels","Designer","_DesignerLibrary.cshtml");
        var contracts=Read(root,"InovaGed.Application","Labels","Canvas","LabelCanvasContracts.cs");
        return Run(output,error,"Label Studio RC37",new Dictionary<string,bool>
        {
            ["PreviewSubjects tenant + ACL"]=controller.Contains("PreviewSubjects")&&controller.Contains("CanAccessGedAsync")&&controller.Contains("tenant_id=@tenantId"),
            ["PreviewSubject sem persistência"]=controller.Contains("PreviewSubject(")&&!Section(controller,"PreviewSubject(","StarterPreview(").Contains("RecordEventAsync"),
            ["design não salvo aceito"]=contracts.Contains("LabelCanvasPreviewSubjectRequest")&&controller.Contains("CopyDesign(design, request.DesignJson)"),
            ["StarterPreview oficial"]=controller.Contains("starters.Create")&&controller.Contains("renderer.Render"),
            ["quatro tabs e blocos"]=library.Contains("data-library-tab=\"blocks\"")&&library.Contains("data-library-tab=\"layers\""),
            ["busca cancelável e debounce"]=client.Contains("previewSearchController?.abort()")&&client.Contains("setTimeout(searchPreviewSubjects,400)"),
            ["diff, conflito e lock preservados"]=controller.Contains("CompareConflict")&&controller.Contains("DESIGN_CONFLICT")&&controller.Contains("ExpectedLockVersion")
        });
    }
    public static int PrintExperience(string root,TextWriter output,TextWriter error)
    {
        var view=Read(root,"InovaGed.Web","Views","Labels","PrintWizard.cshtml");
        var client=Read(root,"InovaGed.Web","wwwroot","js","labels-printwizard.js");
        return Run(output,error,"Impressão RC37",new Dictionary<string,bool>
        {
            ["stepper"]=view.Contains("label-stepper"),
            ["quick preview"]=client.Contains("requestPreview"),
            ["cancelamento de preview"]=client.Contains("AbortController"),
            ["bloqueio de schema"]=client.Contains("previewSchemaBlocked")&&client.Contains("LABEL_SCHEMA_UPDATE_REQUIRED"),
            ["emissão explícita"]=view.Contains("Emitir",StringComparison.OrdinalIgnoreCase)||view.Contains("Imprimir",StringComparison.OrdinalIgnoreCase)
        });
    }
    public static int GedIntake(string root,TextWriter output,TextWriter error)
    {
        var index=Read(root,"InovaGed.Web","Views","GedUploads","Index.cshtml");
        var details=Read(root,"InovaGed.Web","Views","GedUploads","Details.cshtml");
        return Run(output,error,"Central de Entradas RC37",new Dictionary<string,bool>
        {
            ["status traduzidos"]=!index.Contains(">PROCESSING<")&&!index.Contains(">PARTIAL_ERROR<"),
            ["ações em lote"]=index.Contains("bulk",StringComparison.OrdinalIgnoreCase)||details.Contains("selecion",StringComparison.OrdinalIgnoreCase),
            ["conferência"]=details.Contains("Confer",StringComparison.OrdinalIgnoreCase),
            ["classificação"]=details.Contains("Classific",StringComparison.OrdinalIgnoreCase),
            ["integração etiquetas"]=details.Contains("js-label-selection")
        });
    }
    private static string Read(string root,params string[] parts)=>File.ReadAllText(Path.Combine(new[]{root}.Concat(parts).ToArray()));
    private static string Section(string value,string start,string end){var a=value.IndexOf(start,StringComparison.Ordinal);var b=value.IndexOf(end,a+start.Length,StringComparison.Ordinal);return a<0?"":value[a..(b<0?value.Length:b)];}
    private static int Run(TextWriter output,TextWriter error,string name,IReadOnlyDictionary<string,bool> checks){foreach(var x in checks)output.WriteLine($"[{(x.Value?"OK":"FALHA")}] {x.Key}");if(checks.Values.All(x=>x))return 0;error.WriteLine($"[FALHA] {name} incompleto.");return 2;}
}
