namespace InovaGed.Application.HospitalAnalytics;

public static class HospitalTermDictionary
{
    public static readonly IReadOnlyList<TermDictionaryItemDto> All =
    [
        Term("câncer", "ONCOLOGIA", "Alto", "neoplasia", "tumor", "carcinoma", "linfoma", "leucemia", "metástase", "oncologia", "quimioterapia", "radioterapia", "biópsia"),
        Term("sepse", "URGÊNCIA / GRAVIDADE", "Alto", "UTI", "intubação", "parada cardiorrespiratória", "choque", "hemorragia", "trauma", "óbito", "emergência", "urgência"),
        Term("infarto", "CARDIOVASCULAR", "Alto", "IAM", "AVC", "hipertensão", "insuficiência cardíaca", "arritmia", "dor torácica"),
        Term("diabetes", "CRÔNICAS", "Médio", "doença renal crônica", "renal crônico", "DPOC", "hemodiálise", "hipertensão"),
        Term("glosa", "FINANCEIRO / AUDITORIA", "Alto", "nota fiscal", "faturamento", "contrato", "pagamento", "empenho", "orçamento", "compra", "cobrança", "conta hospitalar", "convênio", "SUS", "autorização", "auditoria", "recurso", "procedimento", "diária", "OPME", "medicamento", "material"),
        Term("processo", "JURÍDICO / COMPLIANCE", "Médio", "ofício", "parecer", "notificação", "judicial", "mandado", "determinação", "sindicância", "denúncia"),
        Term("internação", "OPERACIONAL", "Médio", "cirurgia", "alta", "transferência", "regulação", "laudo", "prescrição", "exame", "tomografia", "ultrassom", "ressonância")
    ];

    private static TermDictionaryItemDto Term(string term, string category, string riskLevel, params string[] synonyms) => new()
    {
        Term = term,
        Category = category,
        RiskLevel = riskLevel,
        Synonyms = synonyms
    };
}
