namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsSemanticDiffRc341QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var service = File.ReadAllText(Path.Combine(root, "InovaGed.Infrastructure", "Labels", "LabelCanvasDiffService.cs"));
        var tests = File.ReadAllText(Path.Combine(root, "InovaGed.Application.Tests", "LabelCanvasSemanticDiffRc341Tests.cs"));
        var checks = new Dictionary<string, bool>
        {
            ["API incompatível removida"] = !service.Contains("JsonElement.DeepEquals", StringComparison.Ordinal),
            ["objetos comparados semanticamente"] = service.Contains("SameObject") && service.Contains("TryGetProperty"),
            ["arrays comparados em ordem"] = service.Contains("SameArray") && service.Contains("GetArrayLength"),
            ["números normalizados"] = service.Contains("TryGetDecimal") && service.Contains("TryGetDouble"),
            ["strings ordinais"] = service.Contains("StringComparison.Ordinal"),
            ["booleanos, null e undefined cobertos"] = service.Contains("GetBoolean") && service.Contains("JsonValueKind.Null or JsonValueKind.Undefined"),
            ["casos semânticos testados"] = tests.Contains("different_property_order") && tests.Contains("10_and_10_0") && tests.Contains("different_array_order"),
            ["IDs inválidos testados"] = tests.Contains("duplicate_element_id_is_rejected") && tests.Contains("element_without_id_is_rejected")
        };
        foreach (var check in checks) output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(value => value)) return 0;
        error.WriteLine("[FALHA] Diff semântico RC34.1 incompleto.");
        return 2;
    }
}
