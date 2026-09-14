namespace InovaGed.Environment.Doctor.Checks;

public static class GedIntakeOperationalRc34QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var contract=File.ReadAllText(Path.Combine(root,"InovaGed.Application","Ged","Documents","IDocumentIntakeReviewService.cs"));
        var controller=File.ReadAllText(Path.Combine(root,"InovaGed.Web","Controller","GedUploadsController.cs"));
        var view=File.ReadAllText(Path.Combine(root,"InovaGed.Web","Views","GedUploads","Details.cshtml"));
        var checks=new Dictionary<string,bool>{
            ["intake review possui service real"]=contract.Contains("MarkReviewedAsync")&&contract.Contains("MarkNeedsCorrectionAsync"),
            ["review possui endpoints"]=controller.Contains("/Review\"")&&controller.Contains("ResetPendingAsync"),
            ["status operacionais não usam enum cru"]=view.Contains("OperationalStatus")&&!view.Contains(">@i.Status<"),
            ["bulk classification existe"]=controller.Contains("BulkClassify")&&controller.Contains("_classifications.ApplyAsync"),
            ["post-upload BatchPrint integration existe"]=controller.Contains("LabelSelection")&&controller.Contains("RandomNumberGenerator"),
            ["nenhum alert/prompt principal no Details"]=!view.Contains("alert(")&&!view.Contains("prompt(")};
        foreach(var x in checks)output.WriteLine($"[{(x.Value?"OK":"FALHA")}] {x.Key}");
        if(checks.Values.All(x=>x))return 0;error.WriteLine("[FALHA] Fechamento operacional GED RC34 incompleto.");return 2;
    }
}
