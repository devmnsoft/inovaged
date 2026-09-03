namespace InovaGed.Environment.Doctor.Checks;

public static class ServerLabelsIisQualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var failures = new List<string>();
        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
        void Require(bool condition, string message) { if (!condition) failures.Add(message); }

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

        var preview = Read("InovaGed.Web/Views/Labels/PrintPreview.cshtml");
        Require(preview.Contains("data-label-print-now", StringComparison.Ordinal) && preview.Contains("window.print()", StringComparison.Ordinal), "PrintPreview não possui impressão imediata.");
        Require(File.Exists(Path.Combine(root, "InovaGed.Web/wwwroot/js/labels-print-page.js")), "Script da página de impressão não existe.");

        var webConfig = Read("InovaGed.Web/web.config");
        foreach (var token in new[] { ".env", ".git", "php-cgi", "graphql", "actuator", "server-status", "trace.axd", "@vite", "config.json", "manager.html" })
            Require(webConfig.Contains(token, StringComparison.OrdinalIgnoreCase), $"web.config não bloqueia {token}.");
        Require(File.Exists(Path.Combine(root, "InovaGed.Web/wwwroot/robots.txt")), "robots.txt não existe.");
        Require(!Directory.EnumerateFiles(Path.Combine(root, "InovaGed.Web/wwwroot"), "manager.html", SearchOption.AllDirectories).Any(), "manager.html existe no wwwroot.");

        foreach (var failure in failures) error.WriteLine($"[FALHA] {failure}");
        if (failures.Count > 0) return 2;
        output.WriteLine("[OK] RC18: Data URI, actions de impressão, hardening IIS, robots.txt e ausência de manager.html validados.");
        return 0;
    }
}
