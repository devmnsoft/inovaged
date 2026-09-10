namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsDesignerDimensionRc294QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var view=Read(root,"InovaGed.Web","Views","Labels","Designer","Edit.cshtml");
        var js=Read(root,"InovaGed.Web","wwwroot","js","labels-designer.js");
        var repository=Read(root,"InovaGed.Infrastructure","Labels","LabelCanvasDesignRepository.cs");
        var contracts=Read(root,"InovaGed.Application","Labels","Canvas","LabelCanvasContracts.cs");
        var checks=new Dictionary<string,bool>
        {
            ["inputs number usam invariant"]=view.Contains("CultureInfo.InvariantCulture"),
            ["requestBody não transforma dimensão vazia em zero"]=!js.Contains("Number($('[data-meta=\"widthMm\"]')")&&js.Contains("valueAsNumber"),
            ["canvas é fonte de verdade"]=js.Contains("documentModel.canvas.widthMm=settings.width"),
            ["frontend valida antes do POST"]=js.Contains("validateModelSettings")&&js.Contains("if(!body)"),
            ["backend usa política compartilhada"]=repository.Contains("LabelCanvasDimensionPolicy")&&contracts.Contains("MinWidthMm"),
            ["request e DesignJson são consistentes"]=repository.Contains("DIMENSION_MISMATCH"),
            ["autosave interrompe após 400"]=js.Contains("saveState==='validationError'"),
            ["save é single-flight"]=js.Contains("if(activeSave)return activeSave")
        };
        foreach(var check in checks)output.WriteLine($"[{(check.Value?"OK":"FALHA")}] {check.Key}");
        if(checks.Values.All(x=>x))return 0;error.WriteLine("[FALHA] Contrato RC29.4 incompleto.");return 2;
    }
    private static string Read(string root,params string[] parts)=>File.ReadAllText(Path.Combine(new[]{root}.Concat(parts).ToArray()));
}
