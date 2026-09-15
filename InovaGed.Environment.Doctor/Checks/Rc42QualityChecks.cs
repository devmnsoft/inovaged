namespace InovaGed.Environment.Doctor.Checks;

public static class Rc42QualityChecks
{
    public static int DesignerPreflight(string root, TextWriter output, TextWriter error)
    {
        var script = Read(root, "InovaGed.Web/wwwroot/js/labels-designer.js");
        var view = Read(root, "InovaGed.Web/Views/Labels/Designer/_DesignerValidation.cshtml");
        var toolbar = Read(root, "InovaGed.Web/Views/Labels/Designer/_DesignerToolbar.cshtml");
        var controller = Read(root, "InovaGed.Web/Controller/LabelDesignerController.cs");
        return Run("Smart preflight RC42", output, error, new Dictionary<string, bool>
        {
            ["preflight_panel_selects_problem_element"] = script.Contains("focusProblem(issue.elementId)", StringComparison.Ordinal) && script.Contains("scrollIntoView", StringComparison.Ordinal),
            ["preflight_autofix_requires_confirmation"] = script.Contains("data-autofix-dialog", StringComparison.Ordinal) && script.Contains("returnValue==='confirm'", StringComparison.Ordinal),
            ["preflight_autofix_is_undoable"] = script.Contains("change();commit()", StringComparison.Ordinal) && script.Contains("Use Ctrl+Z para desfazer", StringComparison.Ordinal),
            ["locked elements are protected"] = script.Contains("Desbloqueie o elemento para aplicar esta correção", StringComparison.Ordinal),
            ["accessible designer modes"] = toolbar.Contains("role=\"tablist\"", StringComparison.Ordinal) && toolbar.Contains("role=\"tab\"", StringComparison.Ordinal),
            ["preflight aria live"] = view.Contains("aria-live=\"polite\"", StringComparison.Ordinal),
            ["validation has ACL and no operational side effect"] = controller.Contains("[Authorize(Policy=AppPolicies.LabelDesignerPreview)]", StringComparison.Ordinal) && !ValidationAction(controller).Contains("RecordEventAsync", StringComparison.Ordinal)
        });
    }

    private static string ValidationAction(string source)
    {
        var start = source.IndexOf("ValidateDesign(", StringComparison.Ordinal);
        var end = source.IndexOf("[HttpGet(\"/Labels/Designer/Preview/", start, StringComparison.Ordinal);
        return start >= 0 && end > start ? source[start..end] : "";
    }

    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path));

    private static int Run(string name, TextWriter output, TextWriter error, IReadOnlyDictionary<string, bool> checks)
    {
        foreach (var check in checks) output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(value => value)) return 0;
        error.WriteLine($"[FALHA] {name} incompleto.");
        return 2;
    }
}
