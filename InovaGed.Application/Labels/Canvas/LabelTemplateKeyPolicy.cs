using System.Text.RegularExpressions;

namespace InovaGed.Application.Labels.Canvas;

/// <summary>Single contract for internal Canvas template identities.</summary>
public static partial class LabelTemplateKeyPolicy
{
    public const int MinLength = 3;
    public const int MaxLength = 120;
    public const string GeneratedPrefix = "LBL_";

    public static string Generate(Guid? identity = null)
    {
        var value = identity ?? Guid.NewGuid();
        if (value == Guid.Empty) throw new ArgumentException("A identidade da chave não pode ser vazia.", nameof(identity));
        return GeneratedPrefix + value.ToString("N").ToUpperInvariant();
    }

    public static bool IsValid(string? value) =>
        value is { Length: >= MinLength and <= MaxLength } && AllowedCharacters().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedCharacters();
}
