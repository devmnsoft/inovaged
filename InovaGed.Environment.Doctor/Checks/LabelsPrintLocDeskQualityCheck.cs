namespace InovaGed.Environment.Doctor.Checks;

internal static class LabelsPrintLocDeskQualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var checks = new (string Name, string File, string[] Tokens)[]
        {
            ("manifesto", "database/required_migrations.json", ["2026_09_03_label_print_channel_compat_fix"]),
            ("aplicador consolidado", "database/apply_all_required_migrations.sql", ["2026_09_03_label_print_channel_compat_fix.sql"]),
            ("migration", "database/migrations/2026_09_03_label_print_channel_compat_fix.sql", ["ged.label_print", "print_channel", "ged.label_print_history"]),
            ("registrador schema-aware", "InovaGed.Infrastructure/PhysicalArchive/LabelPrintRegistrar.cs", ["GetColumnsAsync", "available.Contains", "LogLegacyColumns"]),
            ("ações LocDesk", "InovaGed.Web/Views/Labels/LocDesk.cshtml", ["PreviewLocDesk", "PrintLocDesk", "formaction", "type=\"submit\""]),
            ("impressão no navegador", "InovaGed.Web/Views/Labels/LocDeskFolderLabel.cshtml", ["data-label-print-now", "window.print()", "labels-print-page.js"]),
            ("status GET", "InovaGed.Web/Controller/HomeController.cs", ["[HttpGet(\"/status\")]", "[HttpGet(\"/Home/Status\")]", "IActionResult Status()"])
        };

        var failed = false;
        foreach (var check in checks)
        {
            var path = Path.Combine(root, check.File);
            var content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            var missing = check.Tokens.Where(token => !content.Contains(token, StringComparison.Ordinal)).ToArray();
            if (missing.Length == 0) output.WriteLine($"[PASS] {check.Name}: {check.File}");
            else
            {
                failed = true;
                error.WriteLine($"[FAIL] {check.Name}: ausente {string.Join(", ", missing)} em {check.File}");
            }
        }
        return failed ? 2 : 0;
    }
}
