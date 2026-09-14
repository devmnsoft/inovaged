using System.Text.RegularExpressions;

namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsControllerSafetyRc35QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var source = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Controller", "LabelDesignerController.cs"));
        var compare = Regex.Match(
            source,
            @"(?<attributes>(?:\s*\[[^\]]+\])+)\s*public\s+async\s+Task<IActionResult>\s+CompareConflict\b",
            RegexOptions.CultureInvariant).Groups["attributes"].Value;
        var actions = Regex.Matches(
            source,
            @"(?<attributes>(?:\s*\[[^\]]+\])+)\s*public\s+(?:async\s+)?(?:Task<)?IActionResult>?\s+\w+\b",
            RegexOptions.CultureInvariant);

        var duplicateCsrf = actions
            .Cast<Match>()
            .Any(match => Regex.Matches(match.Groups["attributes"].Value, @"\bValidateAntiForgeryToken\b").Count > 1);
        var writeWithoutCsrf = actions
            .Cast<Match>()
            .Select(match => match.Groups["attributes"].Value)
            .Any(attributes => Regex.IsMatch(attributes, @"\bHttp(?:Post|Put|Delete|Patch)\b")
                               && !Regex.IsMatch(attributes, @"\bValidateAntiForgeryToken\b"));

        var checks = new Dictionary<string, bool>
        {
            ["CSRF não duplicado"] = !duplicateCsrf,
            ["CompareConflict com duas rotas"] = Regex.Matches(compare, @"\bHttpPost\b").Count == 2
                                                   && compare.Contains("CompareConflict", StringComparison.Ordinal)
                                                   && compare.Contains("CompareLocal", StringComparison.Ordinal),
            ["CompareConflict com um antiforgery"] = Regex.Matches(compare, @"\bValidateAntiForgeryToken\b").Count == 1,
            ["policy de leitura preservada"] = source.Contains("[Authorize(Policy = AppPolicies.LabelDesignerRead)]", StringComparison.Ordinal),
            ["nenhuma action write sem antiforgery"] = !writeWithoutCsrf
        };

        foreach (var check in checks)
            output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(value => value)) return 0;
        error.WriteLine("[FALHA] Segurança do LabelDesignerController RC35 incompleta.");
        return 2;
    }
}
