using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasFieldCatalogService : ILabelCanvasFieldCatalogService
{
    private static readonly (string Key, string Label)[] BrandingFields =
    [
        ("clientName", "Cliente"), ("contractName", "Contrato"), ("organizationName", "Organização"),
        ("headerTitle", "Título do cabeçalho"), ("headerSubtitle", "Subtítulo do cabeçalho"),
        ("headerExtraLine", "Linha adicional do cabeçalho"), ("footerText", "Rodapé"), ("footerExtraLine", "Linha adicional do rodapé"),
        ("primaryLogo", "Logo principal"), ("secondaryLogo", "Logo secundária")
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<LabelCanvasFieldDto>> Catalog =
        new Dictionary<string, IReadOnlyList<LabelCanvasFieldDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ManualLabel"] = Fields("ManualLabel",
                ("controlNumber", "Nº de controle"),
                ("title", "Título"),
                ("subject", "Assunto"),
                ("description", "Descrição"),
                ("details", "Detalhamento"),
                ("classification", "Classificação"),
                ("documentPeriod", "Período do documento"),
                ("location", "Localização"),
                ("referenceNumber", "Nº de referência"),
                ("observations", "Observações"),
                ("traceCode", "Código de rastreio"),
                ("qrPayload", "Conteúdo do QR Code"),
                ("printedBy", "Impresso por"),
                ("generatedAt", "Gerado em")),
            ["LocDeskFolder"] = Fields("LocDeskFolder",
                ("archiveTitle", "Título do arquivo"), ("contractName", "Contrato"), ("processNumber", "Nº do processo"),
                ("controlNumber", "Nº Controle"), ("volumeNumber", "Volume"), ("volumeTotal", "Total de volumes"),
                ("subject", "Assunto"), ("details", "Detalhamento"), ("activity", "Atividade"),
                ("classification", "Classificação"), ("support", "Suporte"), ("documentPeriod", "Período do Documento"),
                ("currentPhase", "Fase Atual"), ("eliminationForecast", "Previsão Eliminação"),
                ("eliminationStatus", "Situação Eliminação"), ("ledNumber", "Nº LED"), ("location", "Localização"),
                ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code"), ("createdAt", "Criado em"), ("printedBy", "Impresso por")),
            ["LocDeskBox"] = Fields("LocDeskBox",
                ("archiveTitle", "Título do arquivo"), ("contractName", "Contrato"), ("controlNumber", "Nº Controle"),
                ("volumeNumber", "Volume"), ("subject", "Assunto"), ("classification", "Classificação"),
                ("location", "Localização"), ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code")),
            ["Box"] = Fields("Box",
                ("boxCode", "Código da caixa"), ("boxNumber", "Número da caixa"), ("location", "Localização"),
                ("sector", "Setor"), ("classification", "Classificação"), ("periodStart", "Período inicial"),
                ("periodEnd", "Período final"), ("retentionStatus", "Situação de temporalidade"),
                ("custodyStatus", "Situação de custódia"), ("documentCount", "Quantidade de documentos"),
                ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code")),
            ["Document"] = Fields("Document",
                ("documentCode", "Código do documento"), ("documentTitle", "Título do documento"), ("documentType", "Tipo documental"),
                ("classification", "Classificação"), ("folderName", "Pasta"), ("createdAt", "Criado em"),
                ("uploadedBy", "Enviado por"), ("ocrStatus", "Situação do OCR"), ("retentionStatus", "Situação de temporalidade"),
                ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code")),
            ["Folder"] = Fields("Folder", ("documentCode", "Código da pasta"), ("documentTitle", "Título da pasta"), ("classification", "Classificação"), ("location", "Localização"), ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code")),
            ["MedicalRecord"] = Fields("MedicalRecord", ("recordNumber", "Número do prontuário"), ("patientName", "Nome do paciente"), ("classification", "Classificação"), ("location", "Localização"), ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code")),
            ["Process"] = Fields("Process", ("processNumber", "Número do processo"), ("documentTitle", "Assunto do processo"), ("classification", "Classificação"), ("location", "Localização"), ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code")),
            ["PhysicalLocation"] = Fields("PhysicalLocation", ("location", "Localização"), ("sector", "Setor"), ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code")),
            ["Classification"] = Fields("Classification", ("classification", "Classificação"), ("retentionStatus", "Temporalidade"), ("traceCode", "Código de rastreio")),
            ["Loan"] = Fields("Loan", ("controlNumber", "Nº Controle"), ("location", "Localização"), ("currentPhase", "Situação do empréstimo"), ("printedBy", "Responsável"), ("qrPayload", "Conteúdo do QR Code")),
            ["Protocol"] = Fields("Protocol", ("processNumber", "Número do protocolo"), ("subject", "Assunto"), ("createdAt", "Criado em"), ("traceCode", "Código de rastreio"), ("qrPayload", "Conteúdo do QR Code"))
        };

    private static IReadOnlyList<LabelCanvasFieldDto> Fields(string subject, params (string Key, string Label)[] values) =>
        BrandingFields.Concat(values).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
            .Select(x => new LabelCanvasFieldDto(
                x.Key,
                x.Label,
                ResolveDataType(x.Key),
                subject,
                Example(x.Key),
                Category(x.Key),
                $"{x.Label} disponível para composição da etiqueta.",
                IsEditableInManual(x.Key))).ToArray();

    private static string ResolveDataType(string key) => key switch
    {
        "observations" or "details" or "description" => "multiline",
        "createdAt" or "printedAt" or "generatedAt" or "periodStart" or "periodEnd" => "date",
        "primaryLogo" or "secondaryLogo" => "image",
        _ => "text"
    };

    private static bool IsEditableInManual(string key) => key switch
    {
        "clientName" or "contractName" or "organizationName" or "headerTitle" or "headerSubtitle" or
        "headerExtraLine" or "footerText" or "footerExtraLine" or "primaryLogo" or "secondaryLogo" or
        "traceCode" or "qrPayload" or "printedBy" or "generatedAt" or "createdAt" or "uploadedBy" => false,
        _ => true
    };

    private static string Category(string key) => key switch
    {
        "clientName" or "contractName" or "organizationName" or "primaryLogo" or "secondaryLogo" => "Cliente",
        "classification" => "Classificação",
        "periodStart" or "periodEnd" or "retentionStatus" or "documentPeriod" => "Temporalidade",
        "location" or "sector" => "Localização",
        "traceCode" or "qrPayload" => "Rastreabilidade",
        "createdAt" or "printedBy" or "uploadedBy" or "generatedAt" => "Sistema",
        _ => "Identificação"
    };

    private static string? Example(string key) => key switch
    {
        "controlNumber" => "CTRL-2026-0042",
        "title" => "Documento Administrativo",
        "subject" => "Contrato de Prestação de Serviços",
        "description" => "Exercício fiscal de 2026",
        "details" => "Termos aditivos e relatórios mensais",
        "classification" => "ADM.120 - Documentação administrativa",
        "documentPeriod" => "01/01/2026 a 31/12/2026",
        "location" => "Arquivo Central · Estante 03",
        "referenceNumber" => "REF-2026/0199",
        "observations" => "Via única conferida e arquivada.",
        "traceCode" => "MAN-2026-0042",
        _ => null
    };

    public IReadOnlyList<LabelCanvasFieldDto> GetFields(string subjectType)
    {
        var normalized = (subjectType ?? "").Trim();
        if (normalized.Equals("MANUAL_LABEL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ManualLabel", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "ManualLabel";
        }
        return Catalog.TryGetValue(normalized, out var fields) ? fields : [];
    }

    public IReadOnlyDictionary<string, object?> GetSampleData(string profile)
    {
        if (profile.Equals("HOL", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["archiveTitle"] = "ARQUIVO LOCDESCK ANANINDEUA", ["contractName"] = "Hosp. Ophir Loyola",
                ["processNumber"] = "100.334", ["controlNumber"] = "199", ["volumeNumber"] = "___", ["volumeTotal"] = "___",
                ["subject"] = "PRONTUÁRIO nº: 100.334", ["details"] = "DAME - ALTA MEDICA", ["activity"] = "FIM",
                ["classification"] = "HOL.132.3 - LAUDO DE PROCEDIMENTOS DIAGNÓSTICOS", ["support"] = "1. PAPEL",
                ["documentPeriod"] = "15/07/2017 A 25/09/2017", ["currentPhase"] = "2. GUARDA INTERMEDIÁRIA",
                ["eliminationForecast"] = "0. GUARDA PERMANENTE", ["eliminationStatus"] = "0. GUARDA PERMANENTE",
                ["ledNumber"] = "N/A", ["location"] = "LOC.AN.___.E___.P___", ["traceCode"] = "HOL-199-100334",
                ["qrPayload"] = "https://inovaged.local/Labels/Trace/HOL-199-100334", ["createdAt"] = "15/07/2017", ["printedBy"] = "Administrador"
            };

        if (profile.Contains("Caixa", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["archiveTitle"] = "ARQUIVO CENTRAL", ["headerTitle"] = "ARQUIVO CENTRAL", ["clientName"] = "Cliente de demonstração", ["contractName"] = "Contrato de demonstração", ["organizationName"] = "Unidade documental", ["boxCode"] = "CX-2026-0042", ["boxNumber"] = "42",
                ["controlNumber"] = "042", ["sector"] = "Arquivo Central", ["classification"] = "ADM.120 - DOCUMENTAÇÃO ADMINISTRATIVA",
                ["periodStart"] = "01/01/2024", ["periodEnd"] = "31/12/2025", ["retentionStatus"] = "Guarda intermediária",
                ["custodyStatus"] = "No acervo", ["documentCount"] = "87", ["location"] = "LOC.AN.02.E03.P004",
                ["traceCode"] = "CX-2026-0042", ["qrPayload"] = "https://inovaged.local/Labels/Trace/CX-2026-0042"
            };

        if (profile.Equals("ManualLabel", StringComparison.OrdinalIgnoreCase) || profile.Equals("MANUAL_LABEL", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["headerTitle"] = "ARQUIVO CENTRAL", ["clientName"] = "Cliente de demonstração", ["contractName"] = "Contrato administrativo", ["organizationName"] = "Unidade documental",
                ["controlNumber"] = "MAN-2026-0042", ["title"] = "Documento avulso", ["subject"] = "Contrato de prestação de serviços",
                ["description"] = "Exercício 2026", ["details"] = "Termo aditivo e cronograma de entregas",
                ["classification"] = "ADM.120 - DOCUMENTAÇÃO ADMINISTRATIVA", ["documentPeriod"] = "01/01/2026 A 31/12/2026",
                ["location"] = "Arquivo Central · Sala 02", ["referenceNumber"] = "REF-2026/042",
                ["observations"] = "Etiqueta avulsa emitida para identificação externa imediata.",
                ["traceCode"] = "MAN-2026-0042", ["qrPayload"] = "https://inovaged.local/Labels/Trace/MAN-2026-0042",
                ["printedBy"] = "Operador GED", ["generatedAt"] = "10/09/2026"
            };

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["headerTitle"] = "ARQUIVO CENTRAL", ["clientName"] = "Cliente de demonstração", ["contractName"] = "Contrato de demonstração", ["organizationName"] = "Unidade documental",
            ["documentCode"] = "DOC-2026-00199", ["documentTitle"] = "Documento administrativo", ["documentType"] = "Documento",
            ["recordNumber"] = "PR-2026-00199", ["patientName"] = "Pessoa de demonstração", ["processNumber"] = "PROC-2026-00199",
            ["classification"] = "ADM.120 - DOCUMENTAÇÃO ADMINISTRATIVA", ["folderName"] = "Documentação administrativa", ["location"] = "Arquivo Central", ["documentPeriod"] = "01/01/2026 A 31/12/2026", ["createdAt"] = "08/09/2026",
            ["uploadedBy"] = "Equipe GED", ["ocrStatus"] = "Concluído", ["retentionStatus"] = "Guarda permanente",
            ["traceCode"] = "DOC-2026-00199", ["qrPayload"] = "https://inovaged.local/Labels/Trace/DOC-2026-00199"
        };
    }
}
