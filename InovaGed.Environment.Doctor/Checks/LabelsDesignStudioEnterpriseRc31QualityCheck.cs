namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsDesignStudioEnterpriseRc31QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        string Read(params string[] path) => File.ReadAllText(Path.Combine(new[] { root }.Concat(path).ToArray()));
        var index = Read("InovaGed.Web", "Views", "Labels", "Designer", "Index.cshtml");
        var create = Read("InovaGed.Web", "Views", "Labels", "Designer", "New.cshtml");
        var editor = Read("InovaGed.Web", "wwwroot", "js", "labels-designer.js");
        var checks = new Dictionary<string, bool>
        {
            ["galeria do Studio possui cards"] = index.Contains("designer-card") && index.Contains("inovaged.labels.designer.indexView.v1"),
            ["novo modelo usa wizard"] = create.Contains("label-create-wizard"),
            ["favoritos RC30 permanecem"] = editor.Contains("inovaged.labels.designer.favoriteFields.v1"),
            ["dimension safety permanece"] = editor.Contains("validateModelSettings"),
            ["editor possui zoom e layers"] = editor.Contains("zoom", StringComparison.OrdinalIgnoreCase) && editor.Contains("layer", StringComparison.OrdinalIgnoreCase)
        };
        foreach (var check in checks) output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(x => x)) return 0;
        error.WriteLine("[FALHA] Label Design Studio Enterprise RC31 incompleto.");
        return 2;
    }
}
