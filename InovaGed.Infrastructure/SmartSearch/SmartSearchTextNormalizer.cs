using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace InovaGed.Infrastructure.SmartSearch;

public static class SmartSearchTextNormalizer
{
    private static readonly HashSet<string> Preserved = new(StringComparer.OrdinalIgnoreCase) { "apac", "usg", "rx", "tc" };
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase) { "quero", "documento", "documentos", "arquivo", "arquivos", "exame", "o", "a", "os", "as", "de", "do", "da", "dos", "das", "em", "para", "com", "por", "um", "uma" };

    public static string Normalize(string? value)
    {
        var text = RemoveAccents((value ?? string.Empty).Trim()).ToLowerInvariant();
        text = Regex.Replace(text, @"[^\p{L}\p{N}]+", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    public static IReadOnlyList<string> Tokenize(string? value)
        => Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => (t.Length >= 3 || t.All(char.IsDigit) || Preserved.Contains(t)) && !StopWords.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<string> StrongNumbers(string? value)
        => Regex.Matches(value ?? string.Empty, @"\b\d{4,}\b").Select(m => m.Value).Distinct().ToArray();

    private static string RemoveAccents(string value)
    {
        var formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
