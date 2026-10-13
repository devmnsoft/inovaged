namespace InovaGed.Application.Classification;

/// <summary>
/// Versão vigente do plano é a publicação mais recente. O número da versão só desempata.
/// </summary>
public static class PlanVersionSql
{
    public static string CurrentId(string tenantExpression)
    {
        RequireSqlToken(tenantExpression);
        return $"""
(select v_current.id
 from ged.classification_plan_version v_current
 where v_current.tenant_id = {tenantExpression}
   and coalesce(v_current.reg_status, 'A') = 'A'
   and v_current.published_at is not null
 order by v_current.published_at desc, v_current.version_no desc
 limit 1)
""";
    }

    public static string MatchesCurrent(string versionAlias, string tenantExpression)
    {
        RequireSqlToken(versionAlias);
        return $"{versionAlias}.id = {CurrentId(tenantExpression)}";
    }

    private static void RequireSqlToken(string value)
    {
        if (value.Length is 0 or > 40 || value.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '_' and not '.' and not '@'))
            throw new ArgumentException("Expressão SQL de versão do plano inválida.", nameof(value));
    }
}
