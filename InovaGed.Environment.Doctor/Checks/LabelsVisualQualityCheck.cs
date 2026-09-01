using System.Text.RegularExpressions;

namespace InovaGed.Environment.Doctor.Checks;

internal static partial class LabelsVisualQualityCheck
{
    private static readonly string[] RequiredViews =
    [
        "LocDeskFolderLabel.cshtml", "LocDeskBoxLabel.cshtml", "LocDeskFolderHolLabel.cshtml",
        "VisualReview.cshtml", "VisualChecklist.cshtml"
    ];

    private static readonly string[] StandardFields =
    [
        "N° de Controle", "Volume", "Assunto", "Detalhamento", "Atividade", "Classificação",
        "Suporte", "Período do Documento", "Fase Atual", "Previsão Eliminação",
        "Situação Eliminação", "Nº LED", "LOCALIZAÇÃO"
    ];

    public static int Run(string repositoryRoot, TextWriter output, TextWriter error)
    {
        var views = Path.Combine(repositoryRoot, "InovaGed.Web", "Views", "Labels");
        var controller = File.ReadAllText(Path.Combine(repositoryRoot, "InovaGed.Web", "Controller", "LabelsController.cs"));
        var failures = new List<string>();
        foreach (var view in RequiredViews)
            Require(File.Exists(Path.Combine(views, view)), $"View {view} existe", failures, output);

        var printCss = Path.Combine(repositoryRoot, "InovaGed.Web", "wwwroot", "css", "labels-print.css");
        Require(File.Exists(printCss) && File.ReadAllText(printCss).Contains("@media print", StringComparison.Ordinal), "CSS de impressão consolidado existe", failures, output);
        var hol = File.ReadAllText(Path.Combine(views, "LocDeskFolderHolLabel.cshtml"));
        Require(hol.Contains("ARQUIVO LOCDESCK ANANINDEUA", StringComparison.Ordinal), "HOL preserva ARQUIVO LOCDESCK ANANINDEUA", failures, output);
        Require(controller.Contains("Hosp. Ophir Loyola", StringComparison.Ordinal), "Amostra HOL contém Hosp. Ophir Loyola", failures, output);
        var standard = File.ReadAllText(Path.Combine(views, "..", "Shared", "_LocDeskLabel.cshtml"));
        foreach (var field in StandardFields) Require(standard.Contains(field, StringComparison.Ordinal), $"LocDesk padrão contém {field}", failures, output);

        foreach (var file in Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Require(!text.Contains("@media", StringComparison.OrdinalIgnoreCase), $"{Path.GetRelativePath(views, file)} não contém @media", failures, output);
            Require(TitleRegex().Matches(text).Count <= 1, $"{Path.GetRelativePath(views, file)} não duplica Title", failures, output);
            Require(!ButtonWithoutTypeRegex().IsMatch(text), $"{Path.GetRelativePath(views, file)} não contém botão sem type", failures, output);
        }

        Require(controller.Contains("/Labels/VisualReview", StringComparison.Ordinal), "Rota /Labels/VisualReview existe", failures, output);
        Require(controller.Contains("/Labels/VisualChecklist", StringComparison.Ordinal), "Rota /Labels/VisualChecklist existe", failures, output);
        if (failures.Count == 0) { output.WriteLine("Labels visual quality: PASS"); return 0; }
        foreach (var failure in failures) error.WriteLine($"[FAIL] {failure}");
        error.WriteLine($"Labels visual quality: FAIL ({failures.Count})");
        return 2;
    }

    private static void Require(bool condition, string message, ICollection<string> failures, TextWriter output)
    {
        if (condition) output.WriteLine($"[PASS] {message}"); else failures.Add(message);
    }

    [GeneratedRegex("ViewData\\s*\\[\\s*\\\"Title\\\"\\s*\\]\\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<button(?![^>]*\\btype\\s*=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ButtonWithoutTypeRegex();
}
