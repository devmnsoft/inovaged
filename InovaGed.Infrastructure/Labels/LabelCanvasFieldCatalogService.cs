using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasFieldCatalogService : ILabelCanvasFieldCatalogService
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<LabelCanvasFieldDto>> Catalog =
        new Dictionary<string, IReadOnlyList<LabelCanvasFieldDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["LocDeskFolder"] = Fields("LocDeskFolder",
                ("archiveTitle","Título do arquivo"),("contractName","Contrato"),("processNumber","Nº do processo"),
                ("controlNumber","Nº Controle"),("volumeNumber","Volume"),("volumeTotal","Total de volumes"),
                ("subject","Assunto"),("details","Detalhamento"),("activity","Atividade"),
                ("classification","Classificação"),("support","Suporte"),("documentPeriod","Período do Documento"),
                ("currentPhase","Fase Atual"),("eliminationForecast","Previsão Eliminação"),
                ("eliminationStatus","Situação Eliminação"),("ledNumber","Nº LED"),("location","Localização"),
                ("traceCode","Código de rastreio"),("qrPayload","Conteúdo do QR Code"),("createdAt","Criado em"),("printedBy","Impresso por")),
            ["LocDeskBox"] = Fields("LocDeskBox",
                ("archiveTitle","Título do arquivo"),("contractName","Contrato"),("controlNumber","Nº Controle"),
                ("volumeNumber","Volume"),("subject","Assunto"),("classification","Classificação"),
                ("location","Localização"),("traceCode","Código de rastreio"),("qrPayload","Conteúdo do QR Code")),
            ["Box"] = Fields("Box",
                ("boxCode","Código da caixa"),("boxNumber","Número da caixa"),("location","Localização"),
                ("sector","Setor"),("classification","Classificação"),("periodStart","Período inicial"),
                ("periodEnd","Período final"),("retentionStatus","Situação de temporalidade"),
                ("custodyStatus","Situação de custódia"),("documentCount","Quantidade de documentos"),
                ("traceCode","Código de rastreio"),("qrPayload","Conteúdo do QR Code")),
            ["Document"] = Fields("Document",
                ("documentCode","Código do documento"),("documentTitle","Título do documento"),("documentType","Tipo documental"),
                ("classification","Classificação"),("folderName","Pasta"),("createdAt","Criado em"),
                ("uploadedBy","Enviado por"),("ocrStatus","Situação do OCR"),("retentionStatus","Situação de temporalidade"),
                ("traceCode","Código de rastreio"),("qrPayload","Conteúdo do QR Code")),
            ["PhysicalLocation"] = Fields("PhysicalLocation", ("location","Localização"),("sector","Setor"),("traceCode","Código de rastreio"),("qrPayload","Conteúdo do QR Code")),
            ["Classification"] = Fields("Classification", ("classification","Classificação"),("retentionStatus","Temporalidade"),("traceCode","Código de rastreio")),
            ["Loan"] = Fields("Loan", ("controlNumber","Nº Controle"),("location","Localização"),("currentPhase","Situação do empréstimo"),("printedBy","Responsável"),("qrPayload","Conteúdo do QR Code")),
            ["Protocol"] = Fields("Protocol", ("processNumber","Número do protocolo"),("subject","Assunto"),("createdAt","Criado em"),("traceCode","Código de rastreio"),("qrPayload","Conteúdo do QR Code"))
        };

    private static IReadOnlyList<LabelCanvasFieldDto> Fields(string subject, params (string Key, string Label)[] values) =>
        values.Select(x => new LabelCanvasFieldDto(x.Key, x.Label, x.Key.EndsWith("At", StringComparison.Ordinal) ? "date" : "text", subject)).ToArray();

    public IReadOnlyList<LabelCanvasFieldDto> GetFields(string subjectType) =>
        Catalog.TryGetValue(subjectType ?? "", out var fields) ? fields : [];

    public IReadOnlyDictionary<string, object?> GetSampleData(string profile)
    {
        if (profile.Equals("HOL", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["archiveTitle"]="ARQUIVO LOCDESCK ANANINDEUA", ["contractName"]="Hosp. Ophir Loyola",
                ["processNumber"]="100.334", ["controlNumber"]="199", ["volumeNumber"]="___", ["volumeTotal"]="___",
                ["subject"]="PRONTUÁRIO nº: 100.334", ["details"]="DAME - ALTA MEDICA", ["activity"]="FIM",
                ["classification"]="HOL.132.3 - LAUDO DE PROCEDIMENTOS DIAGNÓSTICOS", ["support"]="1. PAPEL",
                ["documentPeriod"]="15/07/2017 A 25/09/2017", ["currentPhase"]="2. GUARDA INTERMEDIÁRIA",
                ["eliminationForecast"]="0. GUARDA PERMANENTE", ["eliminationStatus"]="0. GUARDA PERMANENTE",
                ["ledNumber"]="N/A", ["location"]="LOC.AN.___.E___.P___", ["traceCode"]="HOL-199-100334",
                ["qrPayload"]="https://inovaged.local/Labels/Trace/HOL-199-100334", ["createdAt"]="15/07/2017", ["printedBy"]="Administrador"
            };
        if (profile.Contains("Caixa", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["archiveTitle"]="ARQUIVO LOCDESCK ANANINDEUA", ["boxCode"]="CX-2026-0042", ["boxNumber"]="42",
                ["controlNumber"]="042", ["sector"]="Arquivo Central", ["classification"]="ADM.120",
                ["periodStart"]="01/01/2024", ["periodEnd"]="31/12/2025", ["retentionStatus"]="Guarda intermediária",
                ["custodyStatus"]="No acervo", ["documentCount"]="87", ["location"]="LOC.AN.02.E03.P004",
                ["traceCode"]="CX-2026-0042", ["qrPayload"]="https://inovaged.local/Labels/Trace/CX-2026-0042"
            };
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["documentCode"]="DOC-2026-00199", ["documentTitle"]="Prontuário clínico", ["documentType"]="Prontuário",
            ["classification"]="HOL.132.3", ["folderName"]="Pacientes 2017", ["createdAt"]="15/07/2017",
            ["uploadedBy"]="Equipe GED", ["ocrStatus"]="Concluído", ["retentionStatus"]="Guarda permanente",
            ["traceCode"]="DOC-2026-00199", ["qrPayload"]="https://inovaged.local/Labels/Trace/DOC-2026-00199"
        };
    }
}
