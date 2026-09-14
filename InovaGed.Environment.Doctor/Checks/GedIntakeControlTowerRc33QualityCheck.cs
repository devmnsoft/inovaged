namespace InovaGed.Environment.Doctor.Checks;

public static class GedIntakeControlTowerRc33QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var view = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Views", "GedUploads", "Index.cshtml"));
        var detail = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Views", "GedUploads", "Details.cshtml"));
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "2026_09_11_rc33_collaboration_and_intake.sql"));
        var checks = new Dictionary<string, bool>
        {
            ["histórico existente evoluiu para Central de Entradas"] = view.Contains("Entradas documentais") && view.Contains("data-entry-row"),
            ["status operacionais são traduzidos"] = view.Contains("Em processamento") && view.Contains("Concluído com pendências"),
            ["indicadores têm fonte nos lotes"] = view.Contains("Model.Batches.Sum") && view.Contains("Enviados hoje"),
            ["filtros operacionais existem"] = view.Contains("entrySearch") && view.Contains("entryStatus") && view.Contains("entryFrom"),
            ["seleção reaproveita documentos criados"] = detail.Contains("js-upload-doc-check") && detail.Contains("DocumentId.HasValue"),
            ["conferência possui persistência tenant-aware"] = migration.Contains("document_intake_review") && migration.Contains("tenant_id")
        };
        foreach (var check in checks) output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(x => x)) return 0;
        error.WriteLine("[FALHA] Contrato da Central de Entradas RC33 incompleto."); return 2;
    }
}
