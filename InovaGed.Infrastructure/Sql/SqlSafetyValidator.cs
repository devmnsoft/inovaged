using System.Text.RegularExpressions;
using Dapper;

namespace InovaGed.Infrastructure.Sql;

public static partial class SqlSafetyValidator
{
    private static readonly string[] ForbiddenPatterns =
    [
        "pwhere",
        "dwhere",
        "lwhere",
        "iwhere",
        "where and",
        "and and",
        "or or",
        "fromged",
        "selectfrom",
        "order by and",
        "where order by"
    ];

    public static SqlSafetyValidationResult Validate(string sql, DynamicParameters? parameters = null, bool requireAllSqlParameters = true)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(sql))
        {
            errors.Add("SQL vazio.");
            return new SqlSafetyValidationResult(false, errors, warnings);
        }

        var normalized = WhitespaceRegex().Replace(sql, " ").Trim();
        foreach (var pattern in ForbiddenPatterns)
        {
            if (normalized.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Padrão SQL proibido encontrado: '{pattern}'.");
            }
        }

        if (MissingWhitespaceRegex().Match(sql) is { Success: true } missingWhitespace)
        {
            errors.Add($"Possível ausência de espaço entre palavras SQL próxima de '{missingWhitespace.Value}'.");
        }

        foreach (Match match in StatusEnumWithoutTextCastRegex().Matches(sql))
        {
            errors.Add($"Coluna de status potencialmente enum usada sem ::text: '{match.Value.Trim()}'.");
        }

        var sqlParameterNames = SqlParameterRegex()
            .Matches(sql)
            .Select(m => m.Groups[1].Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (parameters is not null)
        {
            var parameterNames = parameters.ParameterNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (requireAllSqlParameters)
            {
                foreach (var name in sqlParameterNames.Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (!parameterNames.Contains(name))
                    {
                        errors.Add($"Parâmetro usado no SQL não existe no DynamicParameters: @{name}.");
                    }
                }
            }

            foreach (var name in parameterNames.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!sqlParameterNames.Contains(name))
                {
                    warnings.Add($"DynamicParameters contém parâmetro não usado no SQL: @{name}.");
                }
            }
        }

        return new SqlSafetyValidationResult(errors.Count == 0, errors, warnings);
    }

    public static void EnsureValid(string sql, DynamicParameters? parameters = null, bool requireAllSqlParameters = true)
        => Validate(sql, parameters, requireAllSqlParameters).ThrowIfInvalid();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("(?<![@:])@([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex SqlParameterRegex();

    [GeneratedRegex("\\b(select|from|where|and|or|join|left|right|inner|outer|order|group|limit|offset)(select|from|where|and|or|join|left|right|inner|outer|order|group|limit|offset)\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MissingWhitespaceRegex();

    [GeneratedRegex("\\b[a-z][a-z0-9_]*\\.status\\s*(=|<>|!=|in\\b|not\\s+in\\b|ilike\\b|like\\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatusEnumWithoutTextCastRegex();
}
