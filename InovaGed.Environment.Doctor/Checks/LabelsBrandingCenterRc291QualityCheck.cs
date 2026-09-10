namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsBrandingCenterRc291QualityCheck
{
    private static readonly string[] ViewNames = ["Index", "Create", "Edit", "Preview", "Bindings", "TestPrint", "_ProfileForm"];

    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var failures = new List<string>();
        void Check(bool condition, string name)
        {
            if (condition) output.WriteLine($"[OK] {name}");
            else { failures.Add(name); error.WriteLine($"[FALHA] {name}"); }
        }

        var viewRoot = Path.Combine(root, "InovaGed.Web", "Views", "Administration", "PrintBranding");
        var controller = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Controller", "PrintBrandingController.cs"));
        var service = File.ReadAllText(Path.Combine(root, "InovaGed.Infrastructure", "PhysicalArchive", "LabelTemplateService.cs"));
        var form = File.ReadAllText(Path.Combine(viewRoot, "_ProfileForm.cshtml"));
        var help = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Views", "Labels", "Shared", "_LabelHelpDrawer.cshtml"));

        Check(ViewNames.All(name => File.Exists(Path.Combine(viewRoot, $"{name}.cshtml"))), "Views físicas da Central de Identidade Visual existem");
        Check(controller.Contains("~/Views/Administration/PrintBranding/") && ViewNames.Where(x => x != "_ProfileForm").All(name => controller.Contains($"BrandingView(\"{name}\"")), "Actions usam caminhos explícitos de Administration");
        Check(!controller.Contains("return View(") && !controller.Contains("=> View("), "Controller não retorna views implícitas");
        Check(!service.Contains("DUMMY", StringComparison.OrdinalIgnoreCase), "Serviço de templates não possui placeholder DUMMY");
        Check(controller.Contains("tenant_id=@tenant") && controller.Contains("LogosBelong"), "Queries e assets preservam isolamento por tenant");
        Check(controller.Contains("status = 'PUBLISHED'") && controller.Contains("label_template_design_version"), "Canvas usa versão publicada");
        Check(form.Contains("data-branding-editor") && form.Contains("data-logo-select"), "Formulário oferece preview local e logos validadas");
        Check(help.Contains("BRANDING"), "Ajuda contextual de branding disponível");
        output.WriteLine($"RC29.1 Branding Center: {failures.Count} falha(s).");
        return failures.Count == 0 ? 0 : 2;
    }
}
