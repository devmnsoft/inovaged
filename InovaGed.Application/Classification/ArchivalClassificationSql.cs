namespace InovaGed.Application.Classification;

/// <summary>
/// Resolução única da classificação exibida. Código e nome vazios não bloqueiam o fallback,
/// e um vínculo versionado sem item não herda o nome do plano.
/// </summary>
public static class ArchivalClassificationSql
{
    public const string Unclassified = "Não classificado";
    public const string BrokenLink = "Vínculo de classificação inválido";
    public const string Unidentified = "Classe sem identificação";

    public static string Label(string document = "d", string plan = "cp", string item = "pvi")
    {
        RequireAlias(document);
        RequireAlias(plan);
        RequireAlias(item);
        return $"""
CASE
  WHEN {document}.classification_id IS NULL AND {document}.classification_version_id IS NULL THEN '{Unclassified}'
  WHEN {document}.classification_version_id IS NOT NULL AND {item}.classification_id IS NULL THEN '{BrokenLink}'
  WHEN {document}.classification_version_id IS NOT NULL THEN COALESCE(NULLIF(CONCAT_WS(' - ', NULLIF({item}.code, ''), NULLIF({item}.name, '')), ''), '{Unidentified}')
  WHEN {plan}.id IS NULL THEN '{BrokenLink}'
  ELSE COALESCE(NULLIF(CONCAT_WS(' - ', NULLIF({plan}.code, ''), NULLIF({plan}.name, '')), ''), '{Unidentified}')
END
""";
    }

    public static string Code(string document = "d", string plan = "cp", string item = "pvi")
    {
        RequireAlias(document);
        RequireAlias(plan);
        RequireAlias(item);
        return $"""
CASE
  WHEN {document}.classification_version_id IS NOT NULL AND {item}.classification_id IS NULL THEN NULL
  WHEN {document}.classification_version_id IS NOT NULL THEN NULLIF({item}.code, '')
  WHEN {document}.classification_id IS NULL OR {plan}.id IS NULL THEN NULL
  ELSE NULLIF({plan}.code, '')
END
""";
    }

    public static string Name(string document = "d", string plan = "cp", string item = "pvi")
    {
        RequireAlias(document);
        RequireAlias(plan);
        RequireAlias(item);
        return $"""
CASE
  WHEN {document}.classification_id IS NULL AND {document}.classification_version_id IS NULL THEN NULL
  WHEN {document}.classification_version_id IS NOT NULL AND {item}.classification_id IS NULL THEN '{BrokenLink}'
  WHEN {document}.classification_version_id IS NOT NULL THEN COALESCE(NULLIF({item}.name, ''), NULLIF({item}.code, ''), '{Unidentified}')
  WHEN {plan}.id IS NULL THEN '{BrokenLink}'
  ELSE COALESCE(NULLIF({plan}.name, ''), NULLIF({plan}.code, ''), '{Unidentified}')
END
""";
    }

    private static void RequireAlias(string alias)
    {
        if (alias.Length is 0 or > 20 || alias.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
            throw new ArgumentException("Alias SQL de classificação inválido.", nameof(alias));
    }
}
