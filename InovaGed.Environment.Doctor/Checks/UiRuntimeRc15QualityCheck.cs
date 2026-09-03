namespace InovaGed.Environment.Doctor.Checks;

internal static class UiRuntimeRc15QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var checks = new (string Name, string File, string[] Required, string[] Forbidden)[]
        {
            ("migration manifest", "database/required_migrations.json", ["2026_09_03_physical_archive_reg_status_compat_fix"], []),
            ("migration runner", "database/apply_all_required_migrations.sql", ["2026_09_03_physical_archive_reg_status_compat_fix.sql"], []),
            ("physical schema awareness", "InovaGed.Infrastructure/PhysicalArchive2/PhysicalArchive2Service.cs", ["HasColumnAsync", "information_schema.columns", "active[\"physical_box\"]"], []),
            ("PrintWizard native actions", "InovaGed.Web/Views/Labels/PrintWizard.cshtml", ["data-label-form", "formaction=\"@Url.Action(\"Preview\"", "formaction=\"@Url.Action(\"PrintPreview\"", "formaction=\"@Url.Action(\"Print\""], ["href=\"#\""]),
            ("LocDesk native actions", "InovaGed.Web/Views/Labels/LocDesk.cshtml", ["PreviewLocDesk", "PrintLocDesk", "type=\"submit\""], ["href=\"#\""]),
            ("print fallback", "InovaGed.Web/Views/Labels/PrintPreview.cshtml", ["data-label-print-now", "onclick=\"window.print(); return false;\""], []),
            ("label loading recovery", "InovaGed.Web/wwwroot/js/labels-form-submit.js", ["setTimeout", "pageshow", "restore"], []),
            ("critical routes", "InovaGed.Environment.Doctor/quality-routes.json", ["/Labels/History", "/Labels/PrintWizard", "/ClassificationPlan", "/Administration", "/Physical/Dashboard"], [])
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
        var targetViews = new[]
        {
            "InovaGed.Web/Views/Labels/History.cshtml",
            "InovaGed.Web/Views/Labels/PrintWizard.cshtml",
            "InovaGed.Web/Views/ClassificationPlan/Index.cshtml",
            "InovaGed.Web/Views/Administration/Index.cshtml",
            "InovaGed.Web/Views/Physical/Dashboard.cshtml"
        };
        foreach (var relative in targetViews)
        {
            var view = Path.Combine(root, relative);
            if (File.Exists(view) && File.ReadAllText(view).Contains("@media", StringComparison.Ordinal)) { failed = true; error.WriteLine($"[FAIL] CSS responsivo inline: {relative}"); }
        }
        return failed ? 2 : 0;
    }
}
