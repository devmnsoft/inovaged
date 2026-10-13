namespace InovaGed.Application.Sectors;

/// <summary>
/// Catálogo do Anexo I informado no pedido. O PDF não estava no repositório;
/// a nomenclatura segue essa lista, sem transformar os 33 tipos em arquivos obrigatórios.
/// </summary>
public static class SectorCatalog
{
    public const string HospitalInternacao = "HOSPITAL_INTERNACAO";
    public const string Discharge = "DISCHARGE";
    public const string HighCost = "HIGH_COST";
    public const string Surgery = "SURGERY";
    public const string Opme = "OPME";
    public const string WhenApplicable = "WHEN_APPLICABLE";

    public const string Faturamento = "FATURAMENTO";
    public const string Farmacia = "FARMACIA";
    public const string Ddi = "DDI";
    public const string Cme = "CME";
    public const string Selecionado = "SELECIONADO";

    public static readonly string[] SectorCodes = [Faturamento, Farmacia, Ddi, Cme, Selecionado];
    public static readonly string[] EncounterKinds = ["INTERNACAO", "AMBULATORIAL", "URGENCIA"];

    public static readonly IReadOnlyList<SectorCatalogItem> All =
    [
        Item(1, "DS01", "Ficha de Atendimento da Internação.", HospitalInternacao, false, false, Faturamento),
        Item(2, "DS02", "Checklist de Organização do Prontuário Hospitalar.", HospitalInternacao, false, false, Faturamento),
        Item(3, "DS03", "Sumário de Alta Hospitalar.", Discharge, false, false, Faturamento),
        Item(4, "DS04", "Autorização de Procedimentos de Alto Custo — APAC.", HighCost, false, false, Faturamento),
        Item(5, "DS05", "Visita Pré-Anestésica.", Surgery, false, false, Faturamento),
        Item(6, "DS06", "Risco Cirúrgico — Cardiológico.", Surgery, false, false, Faturamento),
        Item(7, "DS07", "Ficha de Anestesia Convencional — Boletim Anestésico.", Surgery, false, false, Faturamento),
        Item(8, "DS08", "Descrição Cirúrgica.", Surgery, true, false, Faturamento, Cme),
        Item(9, "DS09", "Resultado de Exame Histopatológico.", Surgery, true, false, Faturamento),
        Item(10, "DS10", "Registro de Cirurgia e Material Gasto na Sala.", Surgery, true, false, Faturamento, Farmacia, Cme),
        Item(11, "DS11", "Controle de Material Usado em Cirurgia.", Surgery, true, false, Faturamento, Farmacia, Cme),
        Item(12, "DS12", "Nota Fiscal da OPME.", Opme, true, true, Faturamento, Farmacia, Cme),
        Item(13, "DS13", "Documentos Pessoais do Paciente.", WhenApplicable, true, true, Faturamento),
        Item(14, "DS14", "Termo de Responsabilidade assinado pelo paciente ou responsável.", WhenApplicable, false, true, Faturamento),
        Item(15, "DS15", "Termo de Esclarecimento, Informação e Consentimento para Transporte e Análise de Amostra de Material Biológico.", WhenApplicable, false, true, Faturamento),
        Item(16, "DS16", "Termo de Consentimento para Realização de Anestesia.", Surgery, false, true, Faturamento),
        Item(17, "DS17", "Termo de Consentimento para Realização de Procedimento Cirúrgico.", Surgery, false, true, Faturamento),
        Item(18, "DS18", "Documentação e Justificativas relativas à cobrança de diária de longa permanência, acompanhante, UTI, fisioterapia, entre outros.", WhenApplicable, true, false, Faturamento),
        Item(19, "DS19", "Laudo de Internação Hospitalar emitido pelo médico.", HospitalInternacao, false, false, Faturamento),
        Item(20, "DS20", "Evolução Médica.", WhenApplicable, true, false, Faturamento),
        Item(21, "DS21", "Evolução de Enfermagem.", WhenApplicable, true, false, Faturamento),
        Item(22, "DS22", "Evolução Multiprofissional.", WhenApplicable, true, false, Faturamento),
        Item(23, "DS23", "Prescrição Médica.", WhenApplicable, true, false, Faturamento, Farmacia),
        Item(24, "DS24", "Prescrição de Enfermagem.", WhenApplicable, true, false, Faturamento, Farmacia),
        Item(25, "DS25", "Anotações de Enfermagem.", WhenApplicable, true, false, Faturamento),
        Item(26, "DS26", "Gráficos ou Folhas de Registro dos Sinais Vitais.", WhenApplicable, true, false, Faturamento),
        Item(27, "DS27", "Aviso de Cirurgia.", Surgery, true, false, Faturamento, Cme),
        Item(28, "DS28", "Admissão/Indução da Enfermagem.", Surgery, false, false, Faturamento),
        Item(29, "DS29", "Sistematização da Assistência de Enfermagem no Intraoperatório.", Surgery, false, false, Faturamento),
        Item(30, "DS30", "Sistematização da Assistência de Enfermagem — POI.", Surgery, false, false, Faturamento),
        Item(31, "DS31", "Checklist de Cirurgia Segura.", Surgery, false, false, Faturamento),
        Item(32, "DS32", "Formulários de Transferências de Cuidados.", WhenApplicable, true, false, Faturamento),
        Item(33, "DS33", "Resultados de Exames Laboratoriais.", WhenApplicable, true, false, Faturamento)
    ];

    public static readonly string[] AnexoIiCodes = ["DS01", "DS11", "DS12", "DS13", "DS14", "DS15", "DS16", "DS17"];

    public static SectorCatalogItem? Find(string? code)
        => All.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

    private static SectorCatalogItem Item(int order, string code, string name, string applicability, bool multiple, bool physical, params string[] sectors)
        => new(order, code, name, applicability, multiple, physical, sectors);
}

public sealed record SectorCatalogItem(
    int Order,
    string Code,
    string Name,
    string Applicability,
    bool Multiple,
    bool PhysicalOriginal,
    IReadOnlyList<string> ViewSectors);
