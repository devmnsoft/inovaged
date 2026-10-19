namespace InovaGed.Infrastructure.SmartGed;

/// <summary>
/// Expressões da sugestão assistida. A versão do documento é <c>s.version_id</c> quando a coluna existe.
/// <c>pvi.version_id</c> é a versão do plano e não substitui a versão documental da sugestão.
/// </summary>
internal static class SuggestionColumnSql
{
    private static readonly string[] ApplyColumns =
    [
        "version_id",
        "plan_version_id",
        "review_operation_id",
        "processing_started_at",
        "processing_expires_at",
        "applied_at",
        "failed_at",
        "failure_code",
        "public_failure_message"
    ];

    public static string Uuid(HashSet<string> columns, string name) =>
        columns.Contains(name) ? $"s.{name}" : "null::uuid";

    public static string Text(HashSet<string> columns, string name) =>
        columns.Contains(name) ? $"s.{name}" : "null::text";

    public static string Timestamp(HashSet<string> columns, string name) =>
        columns.Contains(name) ? $"s.{name}" : "null::timestamptz";

    public static string LegacyUnproven(HashSet<string> columns)
    {
        if (columns.Contains("is_legacy_unproven") && columns.Contains("version_id"))
            return "coalesce(s.is_legacy_unproven, s.version_id is null)";
        if (columns.Contains("is_legacy_unproven"))
            return "coalesce(s.is_legacy_unproven, true)";
        if (columns.Contains("version_id"))
            return "(s.version_id is null)";
        return "true";
    }

    public static string Obsolete(HashSet<string> columns)
    {
        if (!columns.Contains("version_id"))
            return """
case
    when s.created_at < d.updated_at then true
    else false
end
""";

        return """
case
    when s.version_id is not null and d.current_version_id is not null and s.version_id <> d.current_version_id then true
    when s.version_id is null and s.created_at < d.updated_at then true
    else false
end
""";
    }

    public static string PlanVersionMatch(HashSet<string> columns) =>
        columns.Contains("plan_version_id") ? "pvi.version_id = s.plan_version_id" : "false";

    public static bool CanApplyReview(HashSet<string> columns) =>
        ApplyColumns.All(columns.Contains);
}
