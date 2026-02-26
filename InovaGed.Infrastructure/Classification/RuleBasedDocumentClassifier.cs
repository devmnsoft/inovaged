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
            var suggestedCode = DetectTypeCodeByRules(ocrText);

            if (string.IsNullOrWhiteSpace(suggestedCode))
            {
                result.HasSuggestion = false;
                result.Summary = "Nenhuma regra aplicável.";
                _logger.LogInformation("ClassifyAsync: nenhuma regra aplicável. Doc={DocId}", documentId);
                return result;
            }

            var typeId = await _types.GetIdByCodeAsync(tenantId, suggestedCode, ct);
            if (typeId is null)
            {
                result.HasSuggestion = false;
                result.Summary = $"Código sugerido '{suggestedCode}', mas tipo não encontrado na tabela.";
                _logger.LogWarning("ClassifyAsync: tipo não encontrado. Code={Code}", suggestedCode);
                return result;
            }

            var suggestedSummary = $"Sugerido por regras (code={suggestedCode})";
            var suggestedConfidence = 0.70m;

            const string upsertSql = @"
INSERT INTO ged.document_classification
(
  tenant_id, document_id, document_version_id,
  method, source,
  suggested_type_id, suggested_confidence, suggested_summary, suggested_at,
  reg_status
)
VALUES
(
  @tenantId, @documentId, @documentVersionId,
  @method, @source,
  @suggestedTypeId, @suggestedConfidence, @suggestedSummary, now(),
  'A'
)
ON CONFLICT (tenant_id, document_id)
DO UPDATE SET
  document_version_id      = EXCLUDED.document_version_id,
  method                   = EXCLUDED.method,
  source                   = EXCLUDED.source,
  suggested_type_id        = EXCLUDED.suggested_type_id,
  suggested_confidence     = EXCLUDED.suggested_confidence,
  suggested_summary        = EXCLUDED.suggested_summary,
  suggested_at             = now(),
  reg_status               = 'A';
";

            await using var con = await _db.OpenAsync(ct);
            await con.ExecuteAsync(new CommandDefinition(
                upsertSql,
                new
                {
                    tenantId,
                    documentId,
                    documentVersionId,
                    method = "RULES",
                    source = "OCR",
                    suggestedTypeId = typeId.Value,
                    suggestedConfidence,
                    suggestedSummary
                },
                cancellationToken: ct));

            result.HasSuggestion = true;
            result.SuggestedTypeId = typeId.Value;
            result.SuggestedConfidence = suggestedConfidence;
            result.Summary = suggestedSummary;

            _logger.LogInformation("Sugestão gravada. Doc={DocId} Type={TypeId}", documentId, typeId);
            return result;
        }
        catch (PostgresException pg) when (pg.SqlState == "42P10")
        {
            _logger.LogError(pg,
                "ON CONFLICT falhou: falta UNIQUE (tenant_id, document_id) em ged.document_classification.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na classificação por regras. Doc={DocId}", documentId);
            throw;
        }
    }

    private static string? DetectTypeCodeByRules(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (text.Contains("georreferenciamento", StringComparison.OrdinalIgnoreCase)) return "GEOREF";
        if (text.Contains("contrato", StringComparison.OrdinalIgnoreCase)) return "CONTRATO";
        if (text.Contains("nota fiscal", StringComparison.OrdinalIgnoreCase)) return "NF";

        return null;
    }
}