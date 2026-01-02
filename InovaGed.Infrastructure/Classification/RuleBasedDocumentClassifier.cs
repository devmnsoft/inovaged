using System.Text.RegularExpressions;
using InovaGed.Application.Classification;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class RuleBasedDocumentClassifier : IDocumentClassifier
{
    private readonly IDocumentTypeQueries _types;
    private readonly ILogger<RuleBasedDocumentClassifier> _logger;

    public RuleBasedDocumentClassifier(IDocumentTypeQueries types, ILogger<RuleBasedDocumentClassifier> logger)
    {
        _types = types;
        _logger = logger;
    }

    public async Task<DocumentClassificationResult> ClassifyAsync(
        Guid tenantId,
        Guid documentId,
        Guid documentVersionId,
        string ocrText,
        CancellationToken ct)
    {
        var text = ocrText ?? string.Empty;
        var upper = text.ToUpperInvariant();

        // ====== Scoring por tokens ======
        int scoreContrato = Score(upper, "CONTRATO", "CONTRATANTE", "CONTRATADA", "CLÁUSULA", "CLAUSULA", "OBJETO", "VIGÊNCIA", "VIGENCIA");
        int scoreProc = Score(upper, "PROCURAÇÃO", "PROCURACAO", "OUTORGANTE", "OUTORGADO", "PODERES");
        int scoreNfe = Score(upper, "DANFE", "NFE", "CHAVE DE ACESSO", "PROTOCOLO DE AUTORIZAÇÃO", "PROTOCOLO DE AUTORIZACAO");
        int scoreOficio = Score(upper, "OFÍCIO", "OFICIO", "ASSUNTO:", "ATENCIOSAMENTE", "ILMO", "ILMA");
        int scoreLaudo = Score(upper, "LAUDO", "PACIENTE", "DIAGNÓSTICO", "DIAGNOSTICO", "CID", "CRM");

        var (typeCode, bestScore) = PickBest(new[]
        {
            ("CONTRATO", scoreContrato),
            ("PROCURACAO", scoreProc),
            ("NOTA_FISCAL", scoreNfe),
            ("OFICIO", scoreOficio),
            ("LAUDO", scoreLaudo)
        });

        Guid? typeId = null;
        decimal? confidence = null;

        if (bestScore > 0)
        {
            typeId = await _types.GetIdByCodeAsync(tenantId, typeCode, ct);
            confidence = Normalize(bestScore);
        }

        // ====== Tags ======
        var tags = new List<string>();

        if (typeCode is "CONTRATO" or "PROCURACAO") tags.Add("jurídico");
        if (typeCode is "NOTA_FISCAL") tags.Add("financeiro");
        if (typeCode is "OFICIO") tags.Add("administrativo");
        if (typeCode is "LAUDO") tags.Add("saúde");

        if (upper.Contains("URGENTE") || upper.Contains("PRIORIDADE"))
            tags.Add("urgente");

        // ====== Metadata (regex) ======
        var meta = new Dictionary<string, (string Value, decimal? Confidence)>(StringComparer.OrdinalIgnoreCase);

        // CPF / CNPJ
        var cnpj = Regex.Match(text, @"\b\d{2}\.?\d{3}\.?\d{3}\/?\d{4}\-?\d{2}\b");
        if (cnpj.Success) meta["cnpj"] = (cnpj.Value, 0.85m);

        var cpf = Regex.Match(text, @"\b\d{3}\.?\d{3}\.?\d{3}\-?\d{2}\b");
        if (cpf.Success) meta["cpf"] = (cpf.Value, 0.75m);

        // Nº Ofício / Contrato
        var nOficio = Regex.Match(upper, @"OF[ÍI]CIO\s*N[ºO]?\s*[:\-]?\s*([A-Z0-9\/\.\-]+)");
        if (nOficio.Success) meta["numero_oficio"] = (nOficio.Groups[1].Value, 0.80m);

        var nContrato = Regex.Match(upper, @"CONTRATO\s*N[ºO]?\s*[:\-]?\s*([A-Z0-9\/\.\-]+)");
        if (nContrato.Success) meta["numero_contrato"] = (nContrato.Groups[1].Value, 0.80m);

        // Valor
        var valor = Regex.Match(text, @"R\$\s*\d{1,3}(\.\d{3})*,\d{2}");
        if (valor.Success) meta["valor"] = (valor.Value, 0.70m);

        // Data (bem simples – ajusta se quiser)
        var data = Regex.Match(text, @"\b\d{2}\/\d{2}\/\d{4}\b");
        if (data.Success) meta["data"] = (data.Value, 0.60m);

        var summary = bestScore > 0
            ? $"Classificado por regras: {typeCode} (score={bestScore})."
            : "Sem correspondência suficiente nas regras.";

        _logger.LogInformation("Classificação automática. Doc={DocId}, Ver={VerId}, Type={Type}, Score={Score}",
            documentId, documentVersionId, typeCode, bestScore);

        return new DocumentClassificationResult
        {
            DocumentTypeId = typeId,
            Confidence = confidence,
            Method = "RULES",
            Summary = summary,
            Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Metadata = meta
        };
    }

    private static int Score(string upper, params string[] tokens)
        => tokens.Count(t => upper.Contains(t, StringComparison.OrdinalIgnoreCase)) * 2;

    private static (string Code, int Score) PickBest(IEnumerable<(string Code, int Score)> candidates)
        => candidates.OrderByDescending(c => c.Score).First();

    private static decimal Normalize(int score)
    {
     
        var v = score / 10m;
        if (v < 0.10m) v = 0.10m;
        if (v > 1.00m) v = 1.00m;
        return v;
    }
}
