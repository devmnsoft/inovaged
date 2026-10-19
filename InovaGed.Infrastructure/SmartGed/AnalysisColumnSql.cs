namespace InovaGed.Infrastructure.SmartGed;

/// <summary>
/// Projeção de ged.document_ai_analysis. A versão analisada é version_id da análise.
/// A versão do plano não entra nessa projeção.
/// </summary>
internal static class AnalysisColumnSql
{
    public static string LateralColumns(HashSet<string> columns)
    {
        var version = columns.Contains("version_id") ? "version_id" : "null::uuid as version_id";
        var legacy = columns.Contains("is_legacy_unproven")
            ? "is_legacy_unproven"
            : columns.Contains("version_id")
                ? "(version_id is null) as is_legacy_unproven"
                : "true as is_legacy_unproven";
        return $"id, analysis_status, extracted_summary, extracted_text, confidence, created_at, {version}, {legacy}";
    }

    public static string Order(HashSet<string> columns) =>
        columns.Contains("version_id")
            ? "(a.version_id = d.current_version_id) desc nulls last, a.created_at desc"
            : "a.created_at desc";
}
