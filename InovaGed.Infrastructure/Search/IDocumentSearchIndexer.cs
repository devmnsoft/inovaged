using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Search;

public interface IDocumentSearchIndexer
{
    Task UpsertAsync(
        IDbTransaction tx,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string? code,
        string? title,
        string? description,
        string? fileName,
        string? ocrText,
        CancellationToken ct);
}

public sealed class DocumentSearchIndexer : IDocumentSearchIndexer
{
    private readonly ILogger<DocumentSearchIndexer> _logger;

    public DocumentSearchIndexer(ILogger<DocumentSearchIndexer> logger)
    {
        _logger = logger;
    }

    public async Task UpsertAsync(
        IDbTransaction tx,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string? code,
        string? title,
        string? description,
        string? fileName,
        string? ocrText,
        CancellationToken ct)
    {
        const string sql = @"
SELECT ged.upsert_document_search(
  @tenantId,
  @documentId,
  @versionId,
  @code,
  @title,
  @description,
  @fileName,
  @ocrText
);";

        try
        {
            await tx.Connection!.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    tenantId,
                    documentId,
                    versionId,
                    code = code ?? "",
                    title = title ?? "",
                    description = description ?? "",
                    fileName = fileName ?? "",
                    ocrText = ocrText ?? ""
                },
                tx,
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao atualizar índice de busca. Doc={DocId} Ver={VerId}", documentId, versionId);
            throw;
        }
    }
}
