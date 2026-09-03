namespace InovaGed.Environment.Doctor.Checks;

internal static class UiVisualRc16QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var checks = new (string Name, string File, string[] Required, string[] Forbidden)[]
        {
            ("Labels History", "InovaGed.Web/Views/Labels/History.cshtml", ["label-history-hero", "label-history-kpi-grid", "label-history-toolbar", "label-history-table-shell", "labels-history.css", "Com logo aplicada", "Abrir origem"], ["@media"]),
            ("PrintWizard host", "InovaGed.Web/Views/Labels/PrintWizard.cshtml", ["_PrintWizardFinalReview", "labels-printwizard.css", "data-label-form"], ["href=\"#\"", "@media"]),
            ("PrintWizard final review", "InovaGed.Web/Views/Labels/_PrintWizardFinalReview.cshtml", ["Conferência final", "label-review-tile", "Url.Action(\"Preview\", \"Labels\")", "Url.Action(\"PrintPreview\", \"Labels\")", "Url.Action(\"Print\", \"Labels\")", "type=\"submit\""], ["<table", "href=\"#\"", "@media"]),
            ("Administration", "InovaGed.Web/Views/Administration/Index.cshtml", ["admin-hero", "admin-kpi-grid", "admin-module-grid", "administration-premium.css", "Módulos monitorados"], ["@media"]),
            ("Administration groups", "InovaGed.Web/Controller/AdministrationController.cs", ["Segurança e Acesso", "GED e Operação", "Etiquetas e Impressão", "Sistema e Qualidade"], []),
            ("Classification Plan", "InovaGed.Web/Views/ClassificationPlan/Index.cshtml", ["classification-hero", "classification-kpis", "classification-actions", "classification-empty", "classification-plan.css"], ["@media"]),
            ("History stylesheet", "InovaGed.Web/wwwroot/css/labels-history.css", ["label-history-table-shell", "label-history-empty"], []),
            ("PrintWizard stylesheet", "InovaGed.Web/wwwroot/css/labels-printwizard.css", ["label-final-review__grid", "label-final-actionbar"], [])
        };

        var failed = false;
        foreach (var check in checks)
        {
            var path = Path.Combine(root, check.File);
            var content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            var missing = check.Required.Where(token => !content.Contains(token, StringComparison.Ordinal)).ToArray();
            var forbidden = check.Forbidden.Where(token => content.Contains(token, StringComparison.Ordinal)).ToArray();
            if (missing.Length == 0 && forbidden.Length == 0) output.WriteLine($"[PASS] {check.Name}");
            else { failed = true; error.WriteLine($"[FAIL] {check.Name}: ausentes [{string.Join(", ", missing)}], proibidos [{string.Join(", ", forbidden)}]"); }
        }

        var views = checks.Where(check => check.File.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase));
        foreach (var check in views)
        {
            var content = File.ReadAllText(Path.Combine(root, check.File));
            if (content.Split("ViewData[\"Title\"]", StringSplitOptions.None).Length - 1 > 1)
            {
                failed = true;
                error.WriteLine($"[FAIL] ViewData Title duplicado: {check.File}");
            }
        }
        return failed ? 2 : 0;
    }
}
