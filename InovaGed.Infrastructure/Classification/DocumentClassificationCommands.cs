using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentClassificationCommands : IDocumentClassificationCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentClassificationCommands> _logger;

    public DocumentClassificationCommands(IDbConnectionFactory db, ILogger<DocumentClassificationCommands> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SaveManualAsync(
        Guid tenantId,
        Guid documentId,
        Guid? documentTypeId,
        Guid? userId,
        IReadOnlyList<string> tags,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct)
    {
        var con = await _db.OpenAsync(ct);
        var tx = con.BeginTransaction();

        try
        {
            // 1) UPSERT classificação manual
            const string upsert = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id,
  document_type_id, confidence, method, summary,
  classified_at, classified_by, reg_status
)
VALUES
(
  @DocumentId, @TenantId,
  @DocumentTypeId, NULL, 'MANUAL', NULL,
  now(), @UserId, 'A'
)
ON CONFLICT (document_id)
DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  document_type_id = EXCLUDED.document_type_id,
  confidence = NULL,
  method = 'MANUAL',
  summary = NULL,
  classified_at = now(),
  classified_by = @UserId,
  reg_status = 'A';
";
            await con.ExecuteAsync(new CommandDefinition(upsert, new
            {
                TenantId = tenantId,
                DocumentId = documentId,
                DocumentTypeId = documentTypeId,
                UserId = userId
            }, tx, cancellationToken: ct));

            // 2) TAGS: apaga e insere (manual)
            const string delTags = @"
DELETE FROM ged.document_tag
WHERE tenant_id=@TenantId AND document_id=@DocumentId AND method='MANUAL';
";
            await con.ExecuteAsync(new CommandDefinition(delTags, new { TenantId = tenantId, DocumentId = documentId }, tx, cancellationToken: ct));

            var cleanedTags = tags?
                .Select(t => t?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (cleanedTags.Count > 0)
            {
                // garante tags existentes
                const string ensureTag = @"
INSERT INTO ged.tag (id, tenant_id, name, color, reg_date, reg_status)
VALUES (gen_random_uuid(), @TenantId, @Name, NULL, now(), 'A')
ON CONFLICT DO NOTHING;
";
                foreach (var t in cleanedTags)
                    await con.ExecuteAsync(new CommandDefinition(ensureTag, new { TenantId = tenantId, Name = t! }, tx, cancellationToken: ct));

                const string getIds = @"
SELECT id, name
FROM ged.tag
WHERE tenant_id=@TenantId
  AND lower(name) = ANY(@Names);
";
                var map = (await con.QueryAsync<(Guid id, string name)>(
                        new CommandDefinition(getIds, new
                        {
                            TenantId = tenantId,
                            Names = cleanedTags.Select(x => x.ToLowerInvariant()).ToArray()
                        }, tx, cancellationToken: ct)))
                    .ToDictionary(x => x.name, x => x.id, StringComparer.OrdinalIgnoreCase);

                const string insDocTag = @"
INSERT INTO ged.document_tag (document_id, tag_id, tenant_id, assigned_by, assigned_at, method)
VALUES (@DocumentId, @TagId, @TenantId, @UserId, now(), 'MANUAL');
";

                foreach (var t in cleanedTags)
                {
                    if (!map.TryGetValue(t, out var tagId)) continue;
                    await con.ExecuteAsync(new CommandDefinition(insDocTag, new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        TagId = tagId,
                        UserId = userId
                    }, tx, cancellationToken: ct));
                }
            }

            // 3) METADATA: apaga e insere (manual)
            const string delMeta = @"
DELETE FROM ged.document_metadata
WHERE tenant_id=@TenantId AND document_id=@DocumentId AND method='MANUAL';
";
            await con.ExecuteAsync(new CommandDefinition(delMeta, new { TenantId = tenantId, DocumentId = documentId }, tx, cancellationToken: ct));

            if (metadata is { Count: > 0 })
            {
                const string insMeta = @"
INSERT INTO ged.document_metadata
(document_id, tenant_id, key, value, confidence, method, extracted_at)
VALUES
(@DocumentId, @TenantId, @Key, @Value, NULL, 'MANUAL', now());
";

                foreach (var kv in metadata)
                {
                    var k = kv.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(k)) continue;

                    await con.ExecuteAsync(new CommandDefinition(insMeta, new
                    {
                        TenantId = tenantId,
                        DocumentId = documentId,
                        Key = k,
                        Value = kv.Value ?? ""
                    }, tx, cancellationToken: ct));
                }
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Erro SaveManualAsync Tenant={Tenant} Doc={Doc}", tenantId, documentId);
            throw;
        }
    }

    public async Task ApplySuggestionAsync(
        Guid tenantId,
        Guid documentId,
        Guid suggestedTypeId,
        decimal? suggestedConfidence,
        string? suggestedSummary,
        Guid? userId,
        CancellationToken ct)
    {
        var con = await _db.OpenAsync(ct);

        const string sql = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id,
  document_type_id, confidence, method, summary,
  classified_at, classified_by, reg_status,
  suggested_type_id, suggested_confidence, suggested_summary, suggested_at
)
VALUES
(
  @DocumentId, @TenantId,
  @SuggestedTypeId, @SuggestedConfidence, 'SUGGESTION', @SuggestedSummary,
  now(), @UserId, 'A',
  @SuggestedTypeId, @SuggestedConfidence, @SuggestedSummary, now()
)
ON CONFLICT (document_id)
DO UPDATE SET
  document_type_id = @SuggestedTypeId,
  confidence = @SuggestedConfidence,
  method = 'SUGGESTION',
  summary = @SuggestedSummary,
  classified_at = now(),
  classified_by = @UserId,
  reg_status = 'A',
  suggested_type_id = @SuggestedTypeId,
  suggested_confidence = @SuggestedConfidence,
  suggested_summary = @SuggestedSummary,
  suggested_at = now();
";
        await con.ExecuteAsync(new CommandDefinition(sql, new
        {
            TenantId = tenantId,
            DocumentId = documentId,
            SuggestedTypeId = suggestedTypeId,
            SuggestedConfidence = suggestedConfidence,
            SuggestedSummary = suggestedSummary,
            UserId = userId
        }, cancellationToken: ct));
    }

    public async Task SaveSuggestionOnlyAsync(
    Guid tenantId,
    Guid documentId,
    Guid suggestedTypeId,
    decimal? suggestedConfidence,
    string? suggestedSummary,
    CancellationToken ct)
    {
     var con = await _db.OpenAsync(ct);

        const string sql = @"
INSERT INTO ged.document_classification
(
  document_id, tenant_id,
  suggested_type_id, suggested_confidence, suggested_summary, suggested_at,
  reg_status
)
VALUES
(
  @DocumentId, @TenantId,
  @TypeId, @Conf, @Summary, now(),
  'A'
)
ON CONFLICT (document_id)
DO UPDATE SET
  tenant_id = EXCLUDED.tenant_id,
  suggested_type_id = @TypeId,
  suggested_confidence = @Conf,
  suggested_summary = @Summary,
  suggested_at = now(),
  reg_status = 'A';
";
        await con.ExecuteAsync(new CommandDefinition(sql, new
        {
            TenantId = tenantId,
            DocumentId = documentId,
            TypeId = suggestedTypeId,
            Conf = suggestedConfidence,
            Summary = suggestedSummary
        }, cancellationToken: ct));
    }

}
