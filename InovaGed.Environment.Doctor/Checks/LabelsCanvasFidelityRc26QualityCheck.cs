namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsCanvasFidelityRc26QualityCheck
{
    public static int Run(string root,TextWriter output,TextWriter error)
    {
        var failures=new List<string>();
        string Read(params string[] parts)=>File.ReadAllText(Path.Combine([root,..parts]));
        void Check(bool condition,string name){if(condition)output.WriteLine($"[OK] {name}");else{failures.Add(name);error.WriteLine($"[FALHA] {name}");}}
        var contracts=Read("InovaGed.Application","Labels","Canvas","LabelCanvasContracts.cs");
        var repository=Read("InovaGed.Infrastructure","Labels","LabelCanvasDesignRepository.cs");
        var reader=Read("InovaGed.Infrastructure","Labels","LabelCanvasVersionSnapshotReader.cs");
        var coordinator=Read("InovaGed.Infrastructure","Labels","LabelCanvasPrintCoordinator.cs");
        var renderer=Read("InovaGed.Infrastructure","Labels","LabelCanvasRenderService.cs");
        var migration=Read("database","migrations","2026_09_09_label_canvas_trace_finalization_rc26.sql");
        var compactRepository=repository.Replace(" ","").Replace("\r","").Replace("\n","").Replace("\t","");
        Check(contracts.Contains("LabelCanvasVersionSnapshotDto")&&reader.Contains("LAYOUT_LEGACY"),"Snapshot publicado usa envelope com fallback legado");
        Check(repository.Contains("snapshot_json::text SnapshotJson")&&repository.Contains("LabelCanvasVersionSnapshotReader.Read"),"Versões não dependem de metadata mutável");
        Check(repository.Contains("CancelRevisionAsync")&&compactRepository.Contains("notexists(select1fromged.label_template_design_version"),"Lifecycle de revisão protegido");
        Check(reader.Contains("ComputeVersionHash")&&reader.Contains("VERSION_ENVELOPE_V2"),"VersionHash do envelope");
        Check(contracts.Contains("LabelCanvasExecutionMode")&&!coordinator.Contains("Cliente de demonstração\";"),"Produção separada de demo");
        Check(coordinator.Contains("Snapshot replay exige")&&coordinator.Contains("frozenQr"),"Replay congelado preserva QR");
        Check(contracts.Contains("LabelCanvasSheetLayoutCalculator")||renderer.Contains("LabelCanvasSheetLayoutCalculator"),"Calculador de imposição física");
        Check(renderer.Contains("label-page")&&renderer.Contains("break-after:page"),"Paginação determinística A4/A5/orientation");
        Check(migration.Contains("final_snapshot_json")&&migration.Contains("prepared_trace_id"),"Job possui frozen snapshot por item");
        Check(renderer.Contains("MaxInlineImageBytes"),"Limite de imagem inline");
        output.WriteLine($"RC26 Canvas Fidelity: {failures.Count} falha(s).");return failures.Count==0?0:2;
    }
}
