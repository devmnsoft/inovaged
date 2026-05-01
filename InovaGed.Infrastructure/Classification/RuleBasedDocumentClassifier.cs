using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Classification;

public sealed class RuleBasedDocumentClassifier : IDocumentClassifier
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentTypeQueries _types;
    private readonly ILogger<RuleBasedDocumentClassifier> _logger;

    private const string GenericTypeCode = "DOCUMENTO_GERAL";

    public RuleBasedDocumentClassifier(
        IDbConnectionFactory db,
        IDocumentTypeQueries types,
        ILogger<RuleBasedDocumentClassifier> logger)
    {
        _db = db;
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
        var result = new DocumentClassificationResult
        {
            TenantId = tenantId,
            DocumentId = documentId,
            DocumentVersionId = documentVersionId,
            Method = "RULES",
            Source = "OCR",
            Summary = ""
        };

        try
        {
            var cleanText = NormalizeText(ocrText);

            if (string.IsNullOrWhiteSpace(cleanText))
            {
                result.HasSuggestion = false;
                result.Summary = "OCR executado, porém nenhum texto legível foi extraído.";
                result.Tags.Add("ocr-sem-texto");
                result.Metadata["ocr_status"] = ("OCR executado sem texto legível.", 0.30m);

                _logger.LogWarning("ClassifyAsync: OCR sem texto legível. Doc={DocId}", documentId);
                return result;
            }

            var detected = DetectTypeCodeByRules(cleanText);

            var suggestedCode = detected.Code;
            var confidence = detected.Confidence;
            var reason = detected.Reason;

            if (string.IsNullOrWhiteSpace(suggestedCode))
            {
                suggestedCode = GenericTypeCode;
                confidence = 0.35m;
                reason = "Nenhuma regra específica encontrada. Sugestão genérica gerada a partir do texto OCR.";
            }

            var typeId = await _types.GetIdByCodeAsync(tenantId, suggestedCode, ct);

            if (typeId is null && suggestedCode != GenericTypeCode)
            {
                suggestedCode = GenericTypeCode;
                confidence = 0.30m;
                reason = $"Tipo específico não encontrado. Sugestão genérica aplicada. Regra original: {detected.Code}.";
                typeId = await _types.GetIdByCodeAsync(tenantId, GenericTypeCode, ct);
            }

            if (typeId is null)
            {
                result.HasSuggestion = false;
                result.Summary = "OCR lido, mas nenhum tipo documental foi encontrado. Cadastre o tipo DOCUMENTO_GERAL.";
                result.Tags.Add("ocr-lido");
                result.Tags.Add("sem-tipo-documental");
                result.Metadata["ocr_resumo"] = (BuildDescription(cleanText), 0.40m);

                _logger.LogWarning(
                    "ClassifyAsync: nem o tipo genérico DOCUMENTO_GERAL foi encontrado. Tenant={TenantId}, Doc={DocId}",
                    tenantId,
                    documentId);

                return result;
            }

            var suggestedSummary = $"{reason} Código sugerido: {suggestedCode}.";
            var description = BuildDescription(cleanText);

            await UpsertSuggestionAsync(
                tenantId,
                documentId,
                documentVersionId,
                typeId.Value,
                confidence,
                suggestedSummary,
                ct);

            result.HasSuggestion = true;
            result.DocumentTypeId = typeId.Value;
            result.SuggestedTypeId = typeId.Value;
            result.Confidence = confidence;
            result.SuggestedConfidence = confidence;
            result.Summary = suggestedSummary;

            result.Tags.Add("ocr-lido");
            result.Tags.Add(suggestedCode.ToLowerInvariant());

            result.Metadata["ocr_resumo"] = (description, confidence);
            result.Metadata["ocr_codigo_sugerido"] = (suggestedCode, confidence);
            result.Metadata["ocr_motivo_sugestao"] = (reason, confidence);

            _logger.LogInformation(
                "Sugestão OCR gerada. Doc={DocId}, Type={TypeId}, Code={Code}, Confidence={Confidence}",
                documentId,
                typeId.Value,
                suggestedCode,
                confidence);

            return result;
        }
        catch (PostgresException pg) when (pg.SqlState == "42P10")
        {
            _logger.LogError(
                pg,
                "ON CONFLICT falhou. Verifique constraint única em ged.document_classification.");

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na classificação por regras. Doc={DocId}", documentId);
            throw;
        }
    }

    private async Task UpsertSuggestionAsync(
        Guid tenantId,
        Guid documentId,
        Guid documentVersionId,
        Guid suggestedTypeId,
        decimal suggestedConfidence,
        string suggestedSummary,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO ged.document_classification
(
  document_id,
  tenant_id,
  document_version_id,
  method,
  source,
  suggested_type_id,
  suggested_confidence,
  suggested_summary,
  suggested_at,
  suggested_conf,
  updated_at,
  reg_status
)
VALUES
(
  @documentId,
  @tenantId,
  @documentVersionId,
  'RULES',
  'OCR',
  @suggestedTypeId,
  @suggestedConfidence,
  @suggestedSummary,
  now(),
  @suggestedConfidence,
  now(),
  'A'
)
ON CONFLICT (document_id)
DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  document_version_id = EXCLUDED.document_version_id,
  method = EXCLUDED.method,
  source = EXCLUDED.source,
  suggested_type_id = EXCLUDED.suggested_type_id,
  suggested_confidence = EXCLUDED.suggested_confidence,
  suggested_summary = EXCLUDED.suggested_summary,
  suggested_at = now(),
  suggested_conf = EXCLUDED.suggested_conf,
  updated_at = now(),
  reg_status = 'A';";

        await using var con = await _db.OpenAsync(ct);

        await con.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    tenantId,
                    documentId,
                    documentVersionId,
                    suggestedTypeId,
                    suggestedConfidence,
                    suggestedSummary
                },
                cancellationToken: ct));
    }

    private static (string? Code, decimal Confidence, string Reason) DetectTypeCodeByRules(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, 0m, "Texto OCR vazio.");

        if (ContainsAny(text, "georreferenciamento", "geo referenciamento", "coordenada", "latitude", "longitude"))
            return ("GEOREF", 0.78m, "Foram identificados termos técnicos de georreferenciamento.");

        if (ContainsAny(text, "contrato", "contratante", "contratada", "cláusula", "vigência contratual"))
            return ("CONTRATO", 0.76m, "Foram identificados termos característicos de contrato.");

        if (ContainsAny(text, "nota fiscal", "danfe", "chave de acesso", "valor total da nota", "emitente", "destinatário"))
            return ("NF", 0.80m, "Foram identificados termos característicos de nota fiscal.");

        if (ContainsAny(text, "ofício", "oficio", "memorando", "comunicação interna"))
            return ("OFICIO", 0.68m, "Foram identificados termos de documento administrativo/ofício.");

        if (ContainsAny(text, "requerimento", "solicitação", "solicitacao", "venho requerer", "requer"))
            return ("REQUERIMENTO", 0.68m, "Foram identificados termos de requerimento/solicitação.");

        if (ContainsAny(text, "prontuário", "prontuario", "paciente", "evolução médica", "evolucao medica", "atendimento médico"))
            return ("PRONTUARIO", 0.72m, "Foram identificados termos de prontuário/documento clínico.");

        if (ContainsAny(text, "laudo", "exame", "diagnóstico", "diagnostico", "resultado de exame"))
            return ("LAUDO", 0.70m, "Foram identificados termos de laudo ou exame.");

        if (ContainsAny(text, "relatório", "relatorio", "parecer", "análise", "analise"))
            return ("RELATORIO", 0.62m, "Foram identificados termos de relatório ou parecer.");

        return (null, 0.35m, "Nenhuma regra específica encontrada.");
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return string.Join(
            " ",
            text.Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string BuildDescription(string text)
    {
        var clean = NormalizeText(text);

        if (string.IsNullOrWhiteSpace(clean))
            return "Documento processado por OCR, porém sem texto legível suficiente para descrição automática.";

        if (clean.Length <= 500)
            return clean;

        return clean[..500] + "...";
    }
}