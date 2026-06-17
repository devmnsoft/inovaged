using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using DbDateTime = InovaGed.Application.Common.Database.PostgresDateTimeHelper;
using InovaGed.Application.SmartSearch;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class SmartQueryParser : ISmartQueryParser, InovaGed.Application.Ged.Search.IGedSmartQueryParser
{
    private static readonly Regex AgeRegex = new(@"(?<age>\d{1,3})\s*anos?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AgeRangeRegex = new(@"(?:entre|de)\s*(?<from>\d{1,3})\s*(?:a|e|até|ate)\s*(?<to>\d{1,3})\s*anos?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b(?<year>19\d{2}|20\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex MonthYearRegex = new(@"\b(?<month>0?[1-9]|1[0-2])/(?<year>19\d{2}|20\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex MedicalRecordRegex = new(@"prontu[aá]rio\s*[:\-]?\s*(?<number>\d{3,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProtocolRegex = new(@"protocolo\s*[:\-]?\s*(?<number>\d{3,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StrongNumberRegex = new(@"\b\d{4,}\b", RegexOptions.Compiled);
    private static readonly Regex NameRegex = new(@"(?:paciente|do paciente|da paciente|do|da|de)\s+(?<name>[A-ZÁÉÍÓÚÂÊÔÃÕÇ][\p{L}'´`~-]+(?:\s+(?:da|de|do|dos|das|e|[A-ZÁÉÍÓÚÂÊÔÃÕÇ][\p{L}'´`~-]+)){0,5})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] DocumentWords = ["laudo", "exame", "prontuário", "prontuario", "resultado", "relatório", "relatorio", "receita", "ficha", "guia"];
    private static readonly string[] ExamWords = ["tomografia", "tc", "raio x", "raio-x", "rx", "radiografia", "ressonância", "ressonancia", "ultrassom", "ultrassonografia", "usg", "laboratorial", "laboratório", "laboratorio"];
    private static readonly string[] ClinicalWords = ["avc", "acidente vascular cerebral", "derrame", "diabetes", "diabete", "dm", "doença renal", "doenca renal", "renal", "rim", "rins", "nefrologia", "câncer", "cancer", "neoplasia", "tumor", "cardíaco", "cardiaco", "coração", "coracao", "cardiologia", "pneumonia", "hipertensão", "hipertensao", "gestação", "gestacao", "trauma", "fratura", "infecção", "infeccao"];

    private readonly IDbConnectionFactory _db;
    private readonly ISmartSearchContextParser _contextParser;

    public SmartQueryParser(IDbConnectionFactory db, ISmartSearchContextParser contextParser)
    {
        _db = db;
        _contextParser = contextParser;
    }

    public async Task<SmartSearchIntent> ParseAsync(Guid tenantId, string query, SmartSearchRequest request, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        var normalized = Normalize(query);
        var contextIntent = await _contextParser.ParseAsync(tenantId, query, ct);
        var intent = new SmartSearchIntent
        {
            OriginalQuery = query,
            DocumentType = request.DocumentType,
            From = DbDateTime.ToUtc(request.From),
            To = DbDateTime.ToUtc(request.To)
        };


        var monthYear = MonthYearRegex.Match(query);
        if (monthYear.Success && int.TryParse(monthYear.Groups["month"].Value, out var month) && int.TryParse(monthYear.Groups["year"].Value, out var monthYearValue))
        {
            intent.Year = monthYearValue;
            intent.From = DbDateTime.StartOfDayUtc(new DateTime(monthYearValue, month, 1, 0, 0, 0, DateTimeKind.Utc));
            intent.To = intent.From.Value.AddMonths(1);
        }

        var mr = MedicalRecordRegex.Match(query);
        if (mr.Success) intent.MedicalRecordNumber = mr.Groups["number"].Value;
        var protocol = ProtocolRegex.Match(query);
        if (protocol.Success) intent.ProtocolNumber = protocol.Groups["number"].Value;

        var range = AgeRangeRegex.Match(query);
        if (range.Success && int.TryParse(range.Groups["from"].Value, out var fromAge) && int.TryParse(range.Groups["to"].Value, out var toAge))
        {
            intent.AgeFrom = Math.Min(fromAge, toAge);
            intent.AgeTo = Math.Max(fromAge, toAge);
        }
        else
        {
            var age = AgeRegex.Match(query);
            if (age.Success && int.TryParse(age.Groups["age"].Value, out var exactAge)) intent.Age = exactAge;
        }

        var year = YearRegex.Match(query);
        if (year.Success && int.TryParse(year.Groups["year"].Value, out var y))
        {
            intent.Year = y;
            if (intent.From is null && intent.To is null)
            {
                var isMidYear = normalized.Contains("meados de");
                intent.From = isMidYear
                    ? new DateTime(y, 5, 1, 0, 0, 0, DateTimeKind.Utc)
                    : DbDateTime.YearStartUtc(y);
                intent.To = isMidYear
                    ? new DateTime(y, 9, 1, 0, 0, 0, DateTimeKind.Utc)
                    : DbDateTime.YearEndExclusiveUtc(y);
                intent.IsApproxDate = isMidYear;
            }
        }
        else if (normalized.Contains("ano passado"))
        {
            var lastYear = DateTime.UtcNow.Year - 1;
            intent.Year = lastYear;
            intent.From ??= DbDateTime.YearStartUtc(lastYear);
            intent.To ??= DbDateTime.YearEndExclusiveUtc(lastYear);
            intent.IsApproxDate = true;
        }

        if (intent.From is null && intent.To is null)
        {
            var todayUtc = DateTime.UtcNow.Date;
            if (normalized.Contains("hoje"))
            {
                intent.From = todayUtc;
                intent.To = todayUtc.AddDays(1);
            }
            else if (normalized.Contains("ontem"))
            {
                intent.From = todayUtc.AddDays(-1);
                intent.To = todayUtc;
            }
            else if (normalized.Contains("neste ano") || normalized.Contains("esse ano") || normalized.Contains("este ano"))
            {
                var currentYear = DateTime.UtcNow.Year;
                intent.Year = currentYear;
                intent.From = DbDateTime.YearStartUtc(currentYear);
                intent.To = DbDateTime.YearEndExclusiveUtc(currentYear);
            }
        }

        var name = NameRegex.Match(query);
        if (!string.IsNullOrWhiteSpace(contextIntent.PatientNameHint))
        {
            intent.PatientName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(contextIntent.PatientNameHint.ToLowerInvariant());
            intent.PersonName = intent.PatientName;
        }
        if (string.IsNullOrWhiteSpace(intent.MedicalRecordNumber)) intent.MedicalRecordNumber = contextIntent.MedicalRecordHint;

        if (name.Success && string.IsNullOrWhiteSpace(intent.PatientName))
        {
            var value = Regex.Replace(name.Groups["name"].Value, @"\s+(que|com|tem|em|no|na|internad[oa]|entrou|fala|falam).*$", string.Empty, RegexOptions.IgnoreCase).Trim(' ', ',', '.');
            if (value.Length >= 2)
            {
                intent.PatientName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
                intent.PersonName = intent.PatientName;
            }
        }

        intent.DocumentType = DocumentWords.FirstOrDefault(w => normalized.Contains(Normalize(w))) ?? intent.DocumentType;
        intent.ExamType = ExamWords.FirstOrDefault(w => normalized.Contains(Normalize(w)));
        intent.ClinicalTerms = ClinicalWords.Where(w => normalized.Contains(Normalize(w))).Concat(contextIntent.ClinicalTerms).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (intent.ClinicalTerms.Count > 0) intent.DiseaseTerm = intent.ClinicalTerms[0];
        foreach (var dt in contextIntent.DocumentTypes)
            if (string.IsNullOrWhiteSpace(intent.DocumentType)) intent.DocumentType = dt;

        var termsForSynonyms = intent.ClinicalTerms.ToList();
        if (!string.IsNullOrWhiteSpace(intent.ExamType)) termsForSynonyms.Add(intent.ExamType);
        foreach (var synonym in contextIntent.Synonyms.Concat(contextIntent.RelatedTerms).Concat(await LoadSynonymsAsync(tenantId, termsForSynonyms, ct)))
            if (!intent.ClinicalTerms.Contains(synonym, StringComparer.OrdinalIgnoreCase)) intent.ClinicalTerms.Add(synonym);

        intent.Keywords = BuildKeywords(query, intent).Concat(contextIntent.RequiredTerms).Concat(contextIntent.NumericTokens).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        intent.ExpandedTerms = intent.ClinicalTerms.ToList();
        intent.ExpandedQuery = string.Join(' ', intent.Keywords.Concat(intent.ClinicalTerms).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
        intent.Explanation = contextIntent.ClinicalTerms.Count > 0 ? contextIntent.Explanation : BuildExplanation(intent);
        return intent;
    }

    private async Task<IReadOnlyList<string>> LoadSynonymsAsync(Guid tenantId, IEnumerable<string> terms, CancellationToken ct)
    {
        var values = terms.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0) return [];
        const string sql = """
select synonym from ged.search_synonym
where tenant_id = @tenantId and coalesce(reg_status,'A') = 'A' and lower(term) = any(@terms)
union
select term from ged.search_synonym
where tenant_id = @tenantId and coalesce(reg_status,'A') = 'A' and lower(synonym) = any(@terms)
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return (await conn.QueryAsync<string>(new CommandDefinition(sql, new { tenantId, terms = values.Select(v => v.ToLowerInvariant()).ToArray() }, cancellationToken: ct))).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> BuildKeywords(string query, SmartSearchIntent intent)
    {
        if (!string.IsNullOrWhiteSpace(intent.PatientName)) yield return intent.PatientName;
        if (!string.IsNullOrWhiteSpace(intent.MedicalRecordNumber)) yield return intent.MedicalRecordNumber;
        if (!string.IsNullOrWhiteSpace(intent.ProtocolNumber)) yield return intent.ProtocolNumber;
        if (!string.IsNullOrWhiteSpace(intent.DocumentType)) yield return intent.DocumentType!;
        if (!string.IsNullOrWhiteSpace(intent.ExamType)) yield return intent.ExamType!;
        foreach (Match number in StrongNumberRegex.Matches(query)) yield return number.Value;
        foreach (var word in Regex.Split(query, @"[^\p{L}\p{N}]+"))
            if (word.Length >= 3 && !int.TryParse(word, out _)) yield return word;
    }

    private static string BuildExplanation(SmartSearchIntent intent)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(intent.PatientName)) parts.Add($"nome parecido com {intent.PatientName}");
        if (intent.Age.HasValue) parts.Add($"idade aproximada de {intent.Age} anos");
        if (intent.AgeFrom.HasValue) parts.Add($"idade entre {intent.AgeFrom} e {intent.AgeTo} anos");
        if (!string.IsNullOrWhiteSpace(intent.MedicalRecordNumber)) parts.Add($"prontuário {intent.MedicalRecordNumber}");
        if (!string.IsNullOrWhiteSpace(intent.ProtocolNumber)) parts.Add($"protocolo {intent.ProtocolNumber}");
        if (intent.Year.HasValue) parts.Add($"período de {intent.Year}");
        if (!string.IsNullOrWhiteSpace(intent.DocumentType)) parts.Add($"tipo documental {intent.DocumentType}");
        if (!string.IsNullOrWhiteSpace(intent.ExamType)) parts.Add($"exame {intent.ExamType}");
        if (intent.ClinicalTerms.Count > 0) parts.Add($"termos documentais/clinico-administrativos: {string.Join(", ", intent.ClinicalTerms.Take(4))}");
        return parts.Count == 0 ? "Busca textual ampliada em metadados e OCR." : "Entendi que você procura documentos com " + string.Join("; ", parts) + ".";
    }

    private static string Normalize(string value)
    {
        var formD = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var chars = formD.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
