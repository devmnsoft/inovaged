using System.Text;
using Dapper;

namespace InovaGed.Infrastructure.Sql;

public sealed class SafeSqlBuilder
{
    private readonly StringBuilder _sql = new();
    private bool _hasOrderBy;
    private bool _hasPagination;

    public SafeSqlBuilder(string? baseSql = null)
    {
        if (!string.IsNullOrWhiteSpace(baseSql)) AppendBase(baseSql);
    }

    public SafeSqlBuilder AppendBase(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL base não pode ser vazio.", nameof(sql));
        AppendWithSafeBoundary(sql.TrimEnd());
        return this;
    }

    public SafeSqlBuilder And(string predicate)
    {
        AppendPredicate("and", predicate);
        return this;
    }

    public SafeSqlBuilder AndIf(bool condition, string predicate)
        => condition ? And(predicate) : this;

    public SafeSqlBuilder OrGroup(params string[] predicates)
    {
        var cleaned = predicates.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        if (cleaned.Length == 0) return this;
        AppendPredicate("and", "(" + string.Join(" or ", cleaned) + ")");
        return this;
    }

    public SafeSqlBuilder OrderBy(string orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy)) throw new ArgumentException("ORDER BY não pode ser vazio.", nameof(orderBy));
        var clean = orderBy.Trim();
        if (clean.StartsWith("and ", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("or ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ORDER BY não pode iniciar com AND/OR.");
        }

        AppendWithSafeBoundary(clean.StartsWith("order by", StringComparison.OrdinalIgnoreCase) ? clean : "order by " + clean);
        _hasOrderBy = true;
        return this;
    }

    public SafeSqlBuilder Paginate(string offsetParameter = "Offset", string limitParameter = "Limit")
    {
        if (!_hasOrderBy)
        {
            throw new InvalidOperationException("Paginação exige ORDER BY explícito para resultado determinístico.");
        }

        AppendWithSafeBoundary($"offset @{offsetParameter} limit @{limitParameter}");
        _hasPagination = true;
        return this;
    }

    public string ToSql()
    {
        var sql = _sql.ToString().TrimEnd() + Environment.NewLine;
        SqlSafetyValidator.EnsureValid(sql, requireAllSqlParameters: false);
        return sql;
    }

    public SqlSafetyValidationResult Validate(DynamicParameters? parameters = null, bool requireAllSqlParameters = true)
        => SqlSafetyValidator.Validate(ToSql(), parameters, requireAllSqlParameters);

    public bool HasPagination => _hasPagination;

    private void AppendPredicate(string connector, string predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate)) throw new ArgumentException("Predicado SQL não pode ser vazio.", nameof(predicate));
        var clean = predicate.Trim();
        if (clean.StartsWith("and ", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("or ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Predicados devem ser informados sem AND/OR inicial; use And/OrGroup.");
        }

        var effectiveConnector = HasWhereClause() ? connector : "where";
        AppendWithSafeBoundary($"{effectiveConnector} {clean}");
    }

    private bool HasWhereClause()
        => _sql.ToString().Contains(" where ", StringComparison.OrdinalIgnoreCase)
           || _sql.ToString().Contains(Environment.NewLine + "where ", StringComparison.OrdinalIgnoreCase);

    private void AppendWithSafeBoundary(string fragment)
    {
        if (_sql.Length > 0 && !char.IsWhiteSpace(_sql[_sql.Length - 1])) _sql.AppendLine();
        _sql.AppendLine(fragment);
    }
}
