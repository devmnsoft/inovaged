using System.Text.RegularExpressions;

namespace InovaGed.Application.Classification;

/// <summary>
/// Sugestor simples de tipo documental baseado em texto (OCR, título, nome do arquivo).
/// Regra: pontua tipos por palavras-chave.
/// </summary>
public sealed class SimpleTextDocumentTypeSuggester
{
    private readonly IReadOnlyList<Rule> _rules;

    public SimpleTextDocumentTypeSuggester()
    {
        // Regras básicas (exemplos). Ajuste conforme seus tipos reais.
        _rules = new List<Rule>
        {
            new("NF",        new []{ "nota fiscal", "danfe", "nf-e", "nfe", "xml nfe", "chave de acesso" }, Boost: 1.0m),
            new("CONTRATO",  new []{ "contrato", "contratante", "contratada", "cláusula", "clausula", "vigência", "vigencia", "rescisão", "rescisao" }, Boost: 1.0m),
            new("OFICIO",    new []{ "ofício", "oficio", "atenciosamente", "prezado", "solicitamos", "encaminhamos" }, Boost: 0.9m),
            new("MEMORANDO", new []{ "memorando", "mem", "assunto:", "referência:", "referencia:" }, Boost: 0.9m),
            new("PROCESSO",  new []{ "processo", "autos", "volume", "folha", "tramitação", "tramitacao" }, Boost: 0.7m),
            new("RELATORIO", new []{ "relatório", "relatorio", "análise", "analise", "conclusão", "conclusao", "resultado" }, Boost: 0.6m),
            new("ATA",       new []{ "ata", "reunião", "reuniao", "deliberação", "deliberacao", "pauta" }, Boost: 0.8m),
        };
    }

    public Suggestion Suggest(string? text, IEnumerable<(Guid id, string name)> types)
    {
        var normalized = Normalize(text);

        if (string.IsNullOrWhiteSpace(normalized))
            return Suggestion.Empty();

        var typeList = types?.ToList() ?? new List<(Guid id, string name)>();
        if (typeList.Count == 0)
            return Suggestion.Empty();

        var scored = new List<(string code, decimal score)>();

        foreach (var r in _rules)
        {
            var score = 0m;

            foreach (var kw in r.Keywords)
            {
                if (normalized.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    score += 1m;
            }

            if (score > 0)
            {
                score *= r.Boost;
                scored.Add((r.Code, score));
            }
        }

        if (scored.Count == 0)
            return Suggestion.Empty(method: "TEXT_RULES", confidence: 0m);

        var top = scored.OrderByDescending(x => x.score).First();

        var match = typeList.FirstOrDefault(t =>
            Normalize(t.name).Contains(Normalize(top.code), StringComparison.OrdinalIgnoreCase));

        if (match.id == Guid.Empty)
        {
            match = typeList.FirstOrDefault(t =>
                Normalize(t.name).Contains(Normalize(MapCodeToName(top.code)), StringComparison.OrdinalIgnoreCase));
        }

        var conf = Clamp01(top.score / 6m);

        if (match.id == Guid.Empty)
            return Suggestion.Empty(method: "TEXT_RULES", confidence: conf);

        return new Suggestion(
            SuggestedTypeId: match.id,
            Confidence: conf,
            Method: "TEXT_RULES",
            Summary: $"Sugestão por texto: {match.name}"
        );
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim().ToLowerInvariant();

        // remove pontuação simples
        s = Regex.Replace(s, @"[^\p{L}\p{Nd}\s\-:]", " ");

        // compacta espaços
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static string MapCodeToName(string code)
    {
        return code switch
        {
            "NF" => "nota fiscal",
            "CONTRATO" => "contrato",
            "OFICIO" => "oficio",
            "MEMORANDO" => "memorando",
            "PROCESSO" => "processo",
            "RELATORIO" => "relatorio",
            "ATA" => "ata",
            _ => code
        };
    }

    private static decimal Clamp01(decimal v)
        => v < 0 ? 0 : (v > 1 ? 1 : v);

    private sealed record Rule(string Code, string[] Keywords, decimal Boost);

    public sealed record Suggestion(Guid? SuggestedTypeId, decimal? Confidence, string? Method, string? Summary)
    {
        public static Suggestion Empty(string? method = null, decimal? confidence = null)
            => new(null, confidence, method, null);
    }
}
