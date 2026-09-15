namespace InovaGed.Environment.Doctor.Checks;

public static class Rc46QualityChecks
{
    public static int Automation(string root, TextWriter output, TextWriter error)
    {
        string Read(string path) => File.ReadAllText(Path.Combine(root, path));
        var contracts = Read("InovaGed.Application/Labels/Automation/LabelAutomationContracts.cs");
        var engine = Read("InovaGed.Infrastructure/Labels/LabelAutomationRuleEngine.cs");
        var program = Read("InovaGed.Web/Program.cs");
        var checks = new Dictionary<string, bool>
        {
            ["motor de regras canônico"] = contracts.Contains("ILabelAutomationRuleEngine", StringComparison.Ordinal) && engine.Contains("LabelAutomationRuleEngine", StringComparison.Ordinal),
            ["DI"] = program.Contains("ILabelAutomationRuleEngine", StringComparison.Ordinal),
            ["tenant obrigatório"] = engine.Contains("rule.TenantId != @event.TenantId", StringComparison.Ordinal),
            ["idempotência determinística"] = contracts.Contains("LabelAutomationIdempotency", StringComparison.Ordinal),
            ["simulação sem serviços de impressão"] = !engine.Contains("ILabelPrintJobService", StringComparison.Ordinal) && !engine.Contains("CreateJob", StringComparison.Ordinal),
            ["automação desabilitada por padrão"] = engine.Contains("automaticPrintingAllowed = false", StringComparison.Ordinal),
            ["condições sem SQL livre"] = !contracts.Contains("Sql", StringComparison.OrdinalIgnoreCase),
            ["ausência de entidade fabricada"] = !engine.Contains("Guid.NewGuid", StringComparison.Ordinal),
            ["LocDesk não é modelo de fábrica"] = !engine.Contains("LocDesk", StringComparison.Ordinal)
        };
        foreach (var check in checks) output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(x => x)) return 0;
        error.WriteLine("[FALHA] Doctor RC46 encontrou contratos incompletos."); return 2;
    }
}
