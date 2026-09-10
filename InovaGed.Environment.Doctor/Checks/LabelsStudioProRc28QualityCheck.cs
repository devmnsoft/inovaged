namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsStudioProRc28QualityCheck
{
    public static int Run(string root,TextWriter output,TextWriter error)
    {
        var failures=new List<string>();string Read(params string[] parts)=>File.ReadAllText(Path.Combine(new[]{root}.Concat(parts).ToArray()));
        void Check(bool condition,string name){if(condition)output.WriteLine($"[OK] {name}");else{failures.Add(name);error.WriteLine($"[FALHA] {name}");}}
        var central=Read("InovaGed.Web","Views","Labels","Index.cshtml");var designer=Read("InovaGed.Web","Views","Labels","Designer","Edit.cshtml");var designerJs=Read("InovaGed.Web","wwwroot","js","labels-designer.js");var wizard=Read("InovaGed.Web","Views","Labels","PrintWizard.cshtml");var wizardJs=Read("InovaGed.Web","wwwroot","js","labels-printwizard.js");var controller=Read("InovaGed.Web","Controller","LabelsController.cs");var batch=Read("InovaGed.Web","Views","Labels","BatchPrint.cshtml");var calibration=Read("InovaGed.Web","Views","Labels","Calibration.cshtml");var help=Read("InovaGed.Web","Views","Labels","Shared","_LabelHelpDrawer.cshtml");var starter=Read("InovaGed.Infrastructure","Labels","LabelCanvasStarterTemplateService.cs");
        Check(new[]{"Imprimir etiqueta","Imprimir em lote","Modelos de etiqueta","Histórico e rastreabilidade"}.All(central.Contains),"Central mantém quatro ações principais");
        Check(File.Exists(Path.Combine(root,"InovaGed.Web","Views","Labels","Designer","New.cshtml"))&&!Read("InovaGed.Web","Views","Labels","Designer","New.cshtml").Contains("baseTemplate"),"Criação possui um único assistente");
        Check(!starter.Contains("HOL",StringComparison.OrdinalIgnoreCase)&&!starter.Contains("LocDesk",StringComparison.OrdinalIgnoreCase),"Starter genérico é neutro");
        Check(designerJs.Contains("aria-selected")&&designerJs.Contains("data.librarySection"),"Abas da biblioteca têm estado e teclado");
        Check(designerJs.Contains("data-model-settings-open")&&designer.Contains("Configurações do modelo"),"Configurações do modelo possuem handler");
        Check(!designer.Contains("✓ Tamanho válido")&&designer.Contains("Model.Validation.Issues"),"Checklist usa validação real");
        Check(wizardJs.Contains("currentStep")&&wizardJs.Contains("AbortController")&&wizard.Contains("aria-current"),"PrintWizard possui stepper e cancelamento real");
        Check(controller.Contains("/Labels/PrintWizard/QuickPreview")&&!wizardJs.Contains(".click()"),"QuickPreview dedicado não simula submit");
        Check(File.Exists(Path.Combine(root,"InovaGed.Infrastructure","Labels","ManualLabelInstanceService.cs"))&&File.Exists(Path.Combine(root,"database","migrations","2026_09_10_label_studio_pro_rc28.sql")),"Etiqueta avulsa possui persistência tenant-aware");
        Check(!batch.Contains(">FACTORY<")&&!batch.Contains(">CUSTOM<")&&batch.Contains("data-batch-step=\"3\""),"Lote é guiado e não expõe modos técnicos");
        Check(!calibration.Contains("Labels Print Fidelity")&&calibration.Contains("Ajustes finos"),"Calibração usa linguagem operacional");
        Check(help.Contains("PRINT_STEP_ORIGIN")&&help.Contains("DESIGNER_PUBLISH")&&help.Contains("BATCH_REVIEW"),"Ajuda possui contextos por etapa");
        var changedViews=new[]{"PrintWizard.cshtml","_PrintWizardOrigin.cshtml","_PrintWizardTemplate.cshtml","_PrintWizardBranding.cshtml","_PrintWizardPrintSettings.cshtml","_PrintWizardReview.cshtml","_PrintWizardPreview.cshtml","BatchPrint.cshtml","Calibration.cshtml","Guide.cshtml"};Check(changedViews.All(x=>!Read("InovaGed.Web","Views","Labels",x).Contains("??[]")),"Views alteradas não usam ??[]");
        output.WriteLine($"RC28 Label Studio Pro: {failures.Count} falha(s).");return failures.Count==0?0:2;
    }
}
