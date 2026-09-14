namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsCollaborationGovernanceRc33QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var contracts = File.ReadAllText(Path.Combine(root, "InovaGed.Application", "Labels", "Canvas", "LabelCanvasContracts.cs"));
        var repository = File.ReadAllText(Path.Combine(root, "InovaGed.Infrastructure", "Labels", "LabelCanvasDesignRepository.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Controller", "LabelDesignerController.cs"));
        var client = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "labels-designer.js"));
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "2026_09_11_rc33_collaboration_and_intake.sql"));
        var checks = new Dictionary<string, bool>
        {
            ["token de concorrência integra contrato"] = contracts.Contains("ExpectedLockVersion") && contracts.Contains("LockVersion"),
            ["update é compare-and-swap tenant-aware"] = repository.Contains("lock_version=lock_version+1") && repository.Contains("lock_version=@ExpectedLockVersion"),
            ["save obsoleto retorna 409"] = controller.Contains("DESIGN_CONFLICT") && controller.Contains("return Conflict"),
            ["autosave para e preserva cópia local"] = client.Contains("saveState='conflict'") && client.Contains("localStorage.setItem(localKey") && client.Contains("saveConflict"),
            ["migration aditiva é idempotente"] = migration.Contains("add column if not exists lock_version")
        };
        foreach (var check in checks) output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(x => x)) return 0;
        error.WriteLine("[FALHA] Contrato de colaboração RC33 incompleto."); return 2;
    }
}
