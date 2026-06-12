using System.Text.RegularExpressions;
using InovaGed.Application.SmartSearch;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class DocumentOcrMetadataExtractor : IDocumentOcrMetadataExtractor
{
    private static readonly Regex AgeRegex = new(@"(?<age>\d{1,3})\s*anos?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b(?<year>19\d{2}|20\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex PatientRegex = new(@"(?:paciente|nome)\s*[:\-]?\s*(?<name>[A-ZÁÉÍÓÚÂÊÔÃÕÇ][\p{L}\s]{2,80})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] Terms = ["avc", "acidente vascular cerebral", "diabetes", "doença renal", "doenca renal", "renal", "tomografia", "raio-x", "raio x", "rx", "ressonância", "ultrassom", "laboratório", "laudo", "prontuário"];

    public (int? Age, int? Year, string? PatientName, IReadOnlyList<string> Terms) Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null, null, []);
        int? age = null;
        var ageMatch = AgeRegex.Match(text);
        if (ageMatch.Success && int.TryParse(ageMatch.Groups["age"].Value, out var parsedAge) && parsedAge is > 0 and < 130) age = parsedAge;
        int? year = null;
        var yearMatch = YearRegex.Match(text);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups["year"].Value, out var parsedYear)) year = parsedYear;
        string? patient = null;
        var patientMatch = PatientRegex.Match(text);
        if (patientMatch.Success) patient = patientMatch.Groups["name"].Value.Split('\n', '\r', ',', ';')[0].Trim();
        var lower = text.ToLowerInvariant();
        var terms = Terms.Where(t => lower.Contains(t, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return (age, year, patient, terms);
    }
}
