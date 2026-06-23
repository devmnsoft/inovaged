using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SmartSearch;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class SmartSearchContextParser : ISmartSearchContextParser
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    { "quero", "preciso", "gostaria", "documento", "documentos", "que", "mostre", "pessoas", "pessoa", "pacientes", "paciente", "com", "de", "do", "da", "dos", "das", "para", "um", "uma", "o", "a", "e", "em" };
    private static readonly Regex YearRegex = new(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\b\d{2,}\b", RegexOptions.Compiled);
    private static readonly string[] DocumentTypes = ["laudo", "exame", "prontuário", "relatório médico", "APAC", "autorização", "guia", "anamnese"];

    private readonly IDbConnectionFactory _db;
    public SmartSearchContextParser(IDbConnectionFactory db) => _db = db;

    public async Task<SmartSearchContextIntent> ParseAsync(Guid tenantId, string query, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        var normalized = Normalize(query);
        var intent = new SmartSearchContextIntent { OriginalQuery = query, NormalizedQuery = normalized };
        foreach (Match m in NumberRegex.Matches(query)) intent.NumericTokens.Add(m.Value);
        var ym = YearRegex.Match(query);
        if (ym.Success && int.TryParse(ym.Value, out var year)) { intent.Year = year; intent.FromUtc = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc); intent.ToUtc = intent.FromUtc.Value.AddYears(1); }
        intent.DocumentTypes.AddRange(DocumentTypes.Where(x => normalized.Contains(Normalize(x))));
        var rows = await LoadTermsAsync(tenantId, normalized, ct);
        foreach (var row in rows)
        {
            if (row.Category == "clinical") intent.ClinicalTerms.Add(row.Term);
            if (row.Category == "administrative") intent.AdministrativeTerms.Add(row.Term);
            if (row.Category == "document_type" || row.Category == "exam") intent.DocumentTypes.Add(row.Term);
            intent.MainTerms.Add(row.Term);
            intent.Synonyms.AddRange(row.Synonyms ?? []);
            intent.RelatedTerms.AddRange(row.RelatedTerms ?? []);
            intent.HasSensitiveTerms |= row.IsSensitive;
        }
        foreach (var w in Regex.Split(query, @"[^\p{L}\p{N}]+"))
        {
            if (w.Length < 3 || StopWords.Contains(w)) continue;
            intent.Tokens.Add(w);
            if (int.TryParse(w, out _)) continue;
            intent.OptionalTerms.Add(w);
        }
        if (intent.ClinicalTerms.Any(t => Normalize(t).Contains("cancer") || Normalize(t).Contains("cancer de mama")))
        {
            intent.RequiredTerms.Add("câncer de mama"); intent.RequiredTerms.Add("câncer"); intent.RequiredTerms.Add("mama");
        }
        else intent.RequiredTerms.AddRange(intent.OptionalTerms.Take(3));
        intent.MainTerms = intent.MainTerms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        intent.Tokens = intent.Tokens.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        intent.PatientNameHint = ExtractPatientHint(query);
        intent.MedicalRecordHint = intent.NumericTokens.FirstOrDefault(x => x.Length >= 4 && x != intent.Year?.ToString());
        intent.RequiredTerms = intent.RequiredTerms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        intent.OptionalTerms = intent.OptionalTerms.Concat(intent.Synonyms).Concat(intent.RelatedTerms).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        intent.Explanation = intent.ClinicalTerms.Count > 0
            ? $"Entendi que você procura documentos que mencionem {intent.ClinicalTerms[0]} ou termos relacionados, como {string.Join(" e ", intent.Synonyms.Take(2))}."
            : "Entendi uma busca documental contextual em título, arquivo, pasta, metadados e OCR.";
        return intent;
    }

    private async Task<IReadOnlyList<Row>> LoadTermsAsync(Guid tenantId, string normalized, CancellationToken ct)
    {
        const string sql = """
select term as "Term", category as "Category", synonyms as "Synonyms", related_terms as "RelatedTerms", is_sensitive as "IsSensitive"
from ged.search_context_term
where tenant_id=@tenantId and coalesce(reg_status,'A')='A'
  and (position(normalized_term in @normalized) > 0 or exists(select 1 from unnest(coalesce(synonyms,array[]::text[])) s where position(lower(s) in @normalized) > 0))
""";
        try { await using var c = await _db.OpenAsync(ct); return (await c.QueryAsync<Row>(new CommandDefinition(sql, new { tenantId, normalized }, cancellationToken: ct))).ToList(); }
        catch { return BuiltInTerms(normalized); }
    }
    private static IReadOnlyList<Row> BuiltInTerms(string normalized)
    {
        var rows = new[]
        {
            new Row { Term = "câncer de mama", Category = "clinical", Synonyms = ["neoplasia mamária", "carcinoma mamário", "tumor de mama", "CA de mama", "câncer mamário", "cancer de mama", "oncologia mamária", "mastologia"], RelatedTerms = ["mama", "mamografia", "biópsia", "quimioterapia", "radioterapia", "oncologia"], IsSensitive = true },
            new Row { Term = "APAC", Category = "administrative", Synonyms = ["autorização de procedimento", "autorização de alta complexidade"], RelatedTerms = ["oncologia", "guia", "autorização"], IsSensitive = false },
            new Row { Term = "oncologia", Category = "clinical", Synonyms = ["tratamento oncológico"], RelatedTerms = ["câncer", "neoplasia", "quimioterapia", "radioterapia"], IsSensitive = true },
            new Row { Term = "laudo", Category = "document_type", Synonyms = ["resultado"], RelatedTerms = ["exame"], IsSensitive = false },
            new Row { Term = "exame", Category = "document_type", Synonyms = ["procedimento"], RelatedTerms = ["laudo"], IsSensitive = false },
            new Row { Term = "prontuário", Category = "document_type", Synonyms = ["prontuario", "registro do paciente"], RelatedTerms = ["paciente", "histórico clínico"], IsSensitive = true }
        };
        return rows.Where(r => normalized.Contains(Normalize(r.Term)) || (r.Synonyms ?? []).Any(s => normalized.Contains(Normalize(s)))).ToArray();
    }

    private static string? ExtractPatientHint(string query)
    {
        var m = Regex.Match(query ?? string.Empty, @"(?:paciente|pacient[ea]|prontuário\s+d[ao])\s+([A-ZÁÉÍÓÚÂÊÔÃÕÇ][\p{L}]+(?:\s+[A-ZÁÉÍÓÚÂÊÔÃÕÇ][\p{L}]+){0,3})", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
    private static string Normalize(string value) { var formD = (value ?? string.Empty).Normalize(NormalizationForm.FormD); return new string(formD.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC).ToLowerInvariant(); }
    private sealed class Row { public string Term { get; set; } = ""; public string Category { get; set; } = ""; public string[]? Synonyms { get; set; } public string[]? RelatedTerms { get; set; } public bool IsSensitive { get; set; } }
}
