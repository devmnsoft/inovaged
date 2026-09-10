namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsStudioUxRc27QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var failures = new List<string>();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        void Check(bool condition, string name)
        {
            if (condition) output.WriteLine($"[OK] {name}");
            else { failures.Add(name); error.WriteLine($"[FALHA] {name}"); }
        }

        var central = Read("InovaGed.Web", "Views", "Labels", "Index.cshtml");
        var wizard = Read("InovaGed.Web", "Views", "Labels", "PrintWizard.cshtml");
        var designer = Read("InovaGed.Web", "Views", "Labels", "Designer", "Edit.cshtml");
        var designerJs = Read("InovaGed.Web", "wwwroot", "js", "labels-designer.js");
        Check(File.Exists(Path.Combine(root,"InovaGed.Web","Views","Labels","Guide.cshtml")), "Guia de Etiquetas existe");
        Check(File.Exists(Path.Combine(root,"InovaGed.Web","Views","Labels","Shared","_LabelHelpDrawer.cshtml")), "Drawer de ajuda reutilizável existe");
        Check(central.Contains("actions=new[]") && central.Contains("Configurações") && central.Contains("Modelos antigos"), "Central tem quatro ações e configurações secundárias");
        Check(designer.Contains("Adicionar conteúdo") && designer.Contains("canvas-stage-panel") && designer.Contains("Editar elemento"), "Designer preserva biblioteca, etiqueta e inspetor");
        Check(designer.Contains("Mais opções") && designer.Contains("Configurar modelo"), "Ações e configurações avançadas estão recolhidas");
        Check(wizard.Contains("_PrintWizardOrigin") && wizard.Contains("_PrintWizardReview") && !wizard.Contains("Modo de demonstração"), "PrintWizard sequencial e sem demonstração principal");
        Check(Read("InovaGed.Web","Views","Labels","_PrintWizardBranding.cshtml").Contains("Ajustes avançados da logo"), "Ajustes da logo ficam recolhidos");
        Check(designerJs.Contains("6500") && designerJs.Contains("tourCompleted.v1") && designerJs.Contains("Encontramos alterações locais"), "Autosave, recuperação e tour estão presentes");
        Check(File.Exists(Path.Combine(root,"InovaGed.Web","wwwroot","js","labels-help.js")), "Asset do drawer de ajuda presente");
        output.WriteLine($"RC27 Label Studio UX: {failures.Count} falha(s).");
        return failures.Count == 0 ? 0 : 2;
    }
}
