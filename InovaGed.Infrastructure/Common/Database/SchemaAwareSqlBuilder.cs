namespace InovaGed.Infrastructure.Common.Database;

internal static class SchemaAwareSqlBuilder
{
    public static string CoalesceText(IReadOnlySet<string> columns, params (string Column, string Expression)[] candidates)
    {
        var available = candidates.Where(x => columns.Contains(x.Column)).Select(x => $"nullif({x.Expression}, '')").ToList();
        return available.Count == 0 ? "'Sem informação'" : $"coalesce({string.Join(", ", available)}, 'Sem informação')";
    }

    public static string ColumnOrLiteral(IReadOnlySet<string> columns, string column, string expression, string fallbackLiteral)
        => columns.Contains(column) ? expression : fallbackLiteral;

    public static string SearchPredicate(IReadOnlySet<string> columns, string parameterName, params (string Column, string Expression)[] candidates)
    {
        var predicates = candidates.Where(x => columns.Contains(x.Column)).Select(x => $"{x.Expression} ilike '%' || @{parameterName} || '%'").ToList();
        return predicates.Count == 0 ? "1 = 1" : "(" + string.Join(" or ", predicates) + ")";
    }
}
