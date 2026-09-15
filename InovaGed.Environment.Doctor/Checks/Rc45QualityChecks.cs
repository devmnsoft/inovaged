namespace InovaGed.Environment.Doctor.Checks;

public static class Rc45QualityChecks
{
    public static int OperationalHomologation(string root, TextWriter output, TextWriter error)
    {
        var contracts = Read(root, "InovaGed.Application/Labels/Canvas/LabelCanvasContracts.cs");
        var service = Read(root, "InovaGed.Infrastructure/Labels/LabelPublicationChecklistService.cs");
        var batch = Read(root, "InovaGed.Infrastructure/Labels/LabelOperationalIntelligenceService.cs");
        var repository = Read(root, "InovaGed.Infrastructure/Labels/LabelCanvasDesignRepository.cs");
        var program = Read(root, "InovaGed.Web/Program.cs");
        var checks = new Dictionary<string, bool>
        {
            ["publication checklist contract"] = contracts.Contains("LabelPublicationChecklist", StringComparison.Ordinal),
            ["renderer remains validation authority"] = service.Contains("renderer.Validate", StringComparison.Ordinal),
            ["test lab evidence blocks publication"] = service.Contains("TEST_LAB_REQUIRED", StringComparison.Ordinal),
            ["optimistic concurrency is atomic"] = repository.Contains("lock_version=@expected returning lock_version", StringComparison.Ordinal),
            ["warning justification is audited"] = repository.Contains("request.WarningJustification", StringComparison.Ordinal),
            ["bounded cancellable batch"] = batch.Contains("MaxDegreeOfParallelism", StringComparison.Ordinal) && batch.Contains("CancellationToken", StringComparison.Ordinal),
            ["no preflight print side effect"] = !batch.Contains("CreateJob", StringComparison.Ordinal) && !batch.Contains("RecordEvent", StringComparison.Ordinal),
            ["dependency injection"] = program.Contains("ILabelPublicationChecklistService", StringComparison.Ordinal),
            ["no fabricated subject id"] = !batch.Contains("SubjectId = Guid.Empty", StringComparison.Ordinal),
            ["LocDesk not introduced as factory model"] = !service.Contains("LocDesk", StringComparison.Ordinal)
        };
        foreach (var check in checks) output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(x => x)) return 0;
        error.WriteLine("[FALHA] Homologação operacional RC45 incompleta.");
        return 2;
    }

    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path));
}
