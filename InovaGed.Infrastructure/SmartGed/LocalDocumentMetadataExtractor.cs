using System.Globalization;
using System.Text.RegularExpressions;
using InovaGed.Application.SmartGed;

namespace InovaGed.Infrastructure.SmartGed;

public sealed partial class LocalDocumentMetadataExtractor : IDocumentMetadataExtractor
{
    public Task<DocumentMetadataExtractionResult> ExtractAsync(DocumentMetadataExtractionInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var text = input.Text ?? string.Empty;
        var cpfs = Cpf().Matches(text).Select(m => MaskCpf(m.Value)).Distinct().ToArray();
        var cnpjs = Cnpj().Matches(text).Select(m => MaskCnpj(m.Value)).Distinct().ToArray();
        var processes = Process().Matches(text).Select(m => m.Value).Distinct().ToArray();
        var protocols = Protocol().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToArray();
        var date = Dates().Matches(text).Select(m => ParseDate(m.Value)).FirstOrDefault(d => d.HasValue);
        var normalized = text.ToLowerInvariant();
        var rules = new (string Type, string[] Terms)[]
        {
            ("Documento fiscal", ["nota fiscal", "empenho", "liquidação"]),
            ("Contrato", ["contrato", "vigência", "contratado", "contratante"]),
            ("Documento pessoal", ["cpf", "rg", "certidão", "nascimento"]),
            ("Documento de saúde", ["laudo", "exame", "prontuário", "paciente"])
        };
        var match = rules.Select(r => new { r.Type, Hits = r.Terms.Where(normalized.Contains).ToArray() }).OrderByDescending(x => x.Hits.Length).First();
        var keywords = rules.SelectMany(r => r.Terms).Where(normalized.Contains).Distinct().Take(12).ToArray();
        var sensitive = new List<string>();
        if (cpfs.Length > 0) sensitive.Add("CPF"); if (cnpjs.Length > 0) sensitive.Add("CNPJ");
        if (match.Type == "Documento de saúde") sensitive.Add("DADO_DE_SAUDE");
        var identifiers = new Dictionary<string, IReadOnlyList<string>> { ["cpf"] = cpfs, ["cnpj"] = cnpjs, ["processo"] = processes, ["protocolo"] = protocols };
        var clean = Whitespace().Replace(text, " ").Trim();
        var summary = clean.Length <= 280 ? clean : clean[..280] + "…";
        var confidence = Math.Min(95, 35 + keywords.Length * 8 + identifiers.Values.Count(x => x.Count > 0) * 10);
        return Task.FromResult(new DocumentMetadataExtractionResult(summary, match.Hits.Length == 0 ? null : match.Type, keywords.FirstOrDefault(), date, keywords, identifiers, sensitive, confidence));
    }

    private static DateOnly? ParseDate(string value) => DateOnly.TryParseExact(value, ["dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string MaskCpf(string value) { var d = Digits(value); return d.Length == 11 ? $"***.***.{d[6..9]}-**" : "***"; }
    private static string MaskCnpj(string value) { var d = Digits(value); return d.Length == 14 ? $"**.***.***/{d[8..12]}-**" : "***"; }
    [GeneratedRegex(@"(?<!\d)(?:\d{3}\.?\d{3}\.?\d{3}-?\d{2})(?!\d)")] private static partial Regex Cpf();
    [GeneratedRegex(@"(?<!\d)(?:\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2})(?!\d)")] private static partial Regex Cnpj();
    [GeneratedRegex(@"\b\d{4,7}[.\-]\d{3,7}/\d{4}(?:-\d{1,2})?\b")] private static partial Regex Process();
    [GeneratedRegex(@"(?i)protocolo\s*(?:n[º°o]\.?\s*)?[:#-]?\s*([\w./-]{4,30})")] private static partial Regex Protocol();
    [GeneratedRegex(@"\b(?:\d{2}/\d{2}/\d{4}|\d{4}-\d{2}-\d{2})\b")] private static partial Regex Dates();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}
