namespace InovaGed.Environment.Doctor.Checks;

public static class ServerLabelsIisQualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var failures = new List<string>();
        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
        void Require(bool condition, string message) { if (!condition) failures.Add(message); }

        var packages = Read("Directory.Packages.props");
        var webProject = Read("InovaGed.Web/InovaGed.Web.csproj");
        Require(packages.Contains("System.Diagnostics.DiagnosticSource\" Version=\"9.0.0", StringComparison.Ordinal), "DiagnosticSource 9.0.0 não está fixado centralmente.");
        Require(webProject.Contains("PackageReference Include=\"System.Diagnostics.DiagnosticSource\"", StringComparison.Ordinal), "Projeto de entrada não referencia DiagnosticSource diretamente.");
        var schemaHealth = Read("InovaGed.Infrastructure/SystemHealth/SchemaHealthService.cs");
        Require(schemaHealth.Contains("SchemaHealthStatus.RuntimeDependencyError", StringComparison.Ordinal), "SchemaHealth não diferencia dependência runtime.");
        Require(schemaHealth.Contains("SuggestedScript = null", StringComparison.Ordinal) || !schemaHealth.Contains("SuggestedScript", StringComparison.Ordinal), "Erro runtime ainda pode sugerir migration.");

        var viewsRoot = Path.Combine(root, "InovaGed.Web", "Views");
        foreach (var file in Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(root, file);
            Require(!text.Contains("data:,", StringComparison.OrdinalIgnoreCase), $"{relative} contém fallback data:,.");
            Require(!(text.Contains("Url.Content", StringComparison.Ordinal) && text.Contains("PrintImageSource", StringComparison.Ordinal)), $"{relative} passa PrintImageSource por Url.Content.");
        }

        var printLogo = Read("InovaGed.Web/Views/Shared/Branding/_PrintLogo.cshtml");
        Require(printLogo.Contains("IsValidImageDataUri", StringComparison.Ordinal), "_PrintLogo não valida a Data URI.");
        Require(printLogo.Contains("src=\"@source\"", StringComparison.Ordinal), "_PrintLogo não usa a fonte validada.");

        foreach (var (view, actions) in new[]
        {
            ("InovaGed.Web/Views/Labels/PrintWizard.cshtml", new[] { "Preview", "PrintPreview", "Print" }),
            ("InovaGed.Web/Views/Labels/LocDesk.cshtml", new[] { "PreviewLocDesk", "PrintLocDesk" })
        })
        {
            var text = Read(view);
            Require(text.Contains("data-label-form", StringComparison.Ordinal), $"{view} não identifica o formulário.");
            Require(!text.Contains("action=\"/\"", StringComparison.Ordinal), $"{view} posta para a raiz.");
            foreach (var action in actions)
                Require(text.Contains($"formaction=\"@Url.Action(\"{action}\", \"Labels\")\"", StringComparison.Ordinal), $"{view} não possui formaction para {action}.");
        }

        var submitScript = Read("InovaGed.Web/wwwroot/js/labels-form-submit.js");
        Require(!submitScript.Contains("preventDefault", StringComparison.Ordinal), "Submit tradicional de Labels usa preventDefault.");
        Require(submitScript.Contains("12000", StringComparison.Ordinal), "Submit de Labels não possui recuperação de 12 segundos.");

        var history = Read("InovaGed.Web/Views/Labels/History.cshtml");
        Require(history.Contains("Histórico de Impressões", StringComparison.Ordinal) && history.Contains("label-history-kpi-grid", StringComparison.Ordinal) && history.Contains("label-history-toolbar", StringComparison.Ordinal), "History não contém hero/KPIs/filtros premium.");
        var finalReview = Read("InovaGed.Web/Views/Labels/_PrintWizardFinalReview.cshtml");
        Require(finalReview.Contains("Conferência final", StringComparison.Ordinal) && finalReview.Contains("label-review-tile", StringComparison.Ordinal) && !finalReview.Contains("<table", StringComparison.OrdinalIgnoreCase), "Conferência final não usa cards.");
        var administration = Read("InovaGed.Web/Controller/AdministrationController.cs");
        foreach (var group in new[] { "Segurança e Acesso", "GED e Operação", "Etiquetas e Impressão", "Sistema e Qualidade" })
            Require(administration.Contains(group, StringComparison.Ordinal), $"Administration não contém o grupo {group}.");
        var classification = Read("InovaGed.Web/Views/ClassificationPlan/Index.cshtml");
        Require(classification.Contains("atlas-page-hero", StringComparison.Ordinal) && classification.Contains("classification-kpi", StringComparison.Ordinal), "ClassificationPlan não mantém hero/KPIs.");

        var preview = Read("InovaGed.Web/Views/Labels/PrintPreview.cshtml");
        Require(preview.Contains("data-label-print-now", StringComparison.Ordinal) && preview.Contains("window.print()", StringComparison.Ordinal), "PrintPreview não possui impressão imediata.");
        Require(File.Exists(Path.Combine(root, "InovaGed.Web/wwwroot/js/labels-print-page.js")), "Script da página de impressão não existe.");

        var webConfig = Read("InovaGed.Web/web.config");
        foreach (var token in new[] { ".env", ".git", "php-cgi", "graphql", "actuator", "server-status", "trace.axd", "@vite", "config.json", "manager.html" })
            Require(webConfig.Contains(token, StringComparison.OrdinalIgnoreCase), $"web.config não bloqueia {token}.");
        Require(File.Exists(Path.Combine(root, "InovaGed.Web/wwwroot/robots.txt")), "robots.txt não existe.");
        Require(File.Exists(Path.Combine(root, "InovaGed.Web/wwwroot/favicon.ico")), "favicon.ico não existe.");
        var labelsController = Read("InovaGed.Web/Controller/LabelsController.cs");
        Require(labelsController.Contains("LABEL_ACTION_SUBMITTED", StringComparison.Ordinal) && labelsController.Contains("ClientActionId", StringComparison.Ordinal), "Telemetria correlacionada das etiquetas não existe.");
        Require(!Directory.EnumerateFiles(Path.Combine(root, "InovaGed.Web/wwwroot"), "manager.html", SearchOption.AllDirectories).Any(), "manager.html existe no wwwroot.");

        foreach (var failure in failures) error.WriteLine($"[FALHA] {failure}");
        if (failures.Count > 0) return 2;
        output.WriteLine("[OK] RC20: Data URI, actions de impressão, hardening IIS, robots.txt e ausência de manager.html validados.");
        return 0;
    }
}
