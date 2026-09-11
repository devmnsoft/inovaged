namespace InovaGed.Environment.Doctor.Checks;

public static class GedUploadReliabilityRc311QualityCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var js = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "ged-bulk-upload.js"));
        var view = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Views", "Ged", "Index.cshtml"));
        var controller = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Controller", "GedController.cs"));
        var checks = new Dictionary<string, bool>
        {
            ["falhas usam predicado central"] = js.Contains("isUploadFailure(fileItem)") && js.Contains("FAILURE_STATUSES"),
            ["abort, timeout e cancelamento são falhas"] = js.Contains("'aborted', 'timeout', 'cancelled'") && js.Contains("fileItem.status = 'timeout'"),
            ["erro HTTP e não JSON é normalizado"] = js.Contains("normalizeUploadError") && js.Contains("413:") && js.Contains("UNEXPECTED_SERVER_RESPONSE"),
            ["alerta persistente e acessível existe"] = view.Contains("id=\"bulkUploadMessage\"") && js.Contains("aria-live"),
            ["classificação chega ao upload normal e chunk"] = js.Contains("fd.append('classificationId'") && js.Contains("metadata: { classificationId:"),
            ["ações documentais usam documentId válido"] = js.Contains("buildDocumentQuickActions") && js.Contains("subjectType=DOCUMENT&subjectId=${encoded}"),
            ["correlationId fica visível"] = view.Contains("uploadErrorCorrelationId") && js.Contains("payload?.correlationId"),
            ["exceção bruta não sai no upload"] = !controller.Contains("JsonError(\"Erro interno ao enviar arquivo.\", \"Servidor\", ex.Message")
        };
        foreach (var check in checks) output.WriteLine($"[{(check.Value ? "OK" : "FALHA")}] {check.Key}");
        if (checks.Values.All(x => x)) return 0;
        error.WriteLine("[FALHA] Contrato de confiabilidade RC31.1 incompleto.");
        return 2;
    }
}
