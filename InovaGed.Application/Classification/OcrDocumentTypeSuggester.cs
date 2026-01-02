
namespace InovaGed.Application.Classification;

public sealed class OcrDocumentTypeSuggester
{
    // ✅ ajuste as palavras e os "codes" / IDs conforme seu catálogo
    private static readonly (string[] Keywords, string TypeCode, decimal Conf)[] Rules =
    [
        (["cpf", "receita federal", "cadastro de pessoa física"], "CPF", 0.85m),
        (["cnpj", "receita federal", "cadastro nacional"], "CNPJ", 0.85m),
        (["nota fiscal", "danfe", "chave de acesso"], "NOTA_FISCAL", 0.90m),
        (["contrato", "cláusula", "contratante", "contratada"], "CONTRATO", 0.80m),
        (["procuração", "outorgante", "outorgado", "poderes"], "PROCURACAO", 0.85m),
        (["relatório", "conclusão", "sumário", "metodologia"], "RELATORIO", 0.70m),
    ];

    public (string? TypeCode, decimal Confidence) Suggest(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return (null, 0m);

        var t = ocrText.ToLowerInvariant();

        foreach (var r in Rules)
        {
            if (r.Keywords.Any(k => t.Contains(k)))
                return (r.TypeCode, r.Conf);
        }

        return (null, 0m);
    }
}
