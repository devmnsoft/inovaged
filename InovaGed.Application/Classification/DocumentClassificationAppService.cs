using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InovaGed.Application.Documents;
using Microsoft.Extensions.Logging;

namespace InovaGed.Application.Classification;

public sealed class DocumentClassificationAppService
{
    private readonly ILogger<DocumentClassificationAppService> _logger;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDocumentClassificationQueries _queries;
    private readonly IDocumentClassificationCommands _commands;
    private readonly IDocumentSearchTextQueries _searchText;
    private readonly SimpleTextDocumentTypeSuggester _suggester;

    public DocumentClassificationAppService(
        ILogger<DocumentClassificationAppService> logger,
        ICurrentUserAccessor currentUser,
        IDocumentClassificationQueries queries,
        IDocumentClassificationCommands commands,
        IDocumentSearchTextQueries searchText,
        SimpleTextDocumentTypeSuggester suggester)
    {
        _logger = logger;
        _currentUser = currentUser;
        _queries = queries;
        _commands = commands;
        _searchText = searchText;
        _suggester = suggester;
    }

    public Task<DocumentClassificationViewDto?> GetAsync(Guid documentId, CancellationToken ct)
        => _queries.GetAsync(_currentUser.TenantId, documentId, ct);

    public async Task SaveManualAsync(
        Guid documentId,
        Guid? documentTypeId,
        string? tagsCsv,
        string? metadataLines,
        CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var dto = await _queries.GetAsync(tenantId, documentId, ct);
        if (dto is null)
            throw new InvalidOperationException("Documento não encontrado para classificar.");

        var tags = ParseTags(tagsCsv);
        var meta = ParseMetadata(metadataLines);

        await _commands.SaveManualAsync(
            tenantId: tenantId,
            documentId: documentId,
            documentTypeId: documentTypeId,
            userId: userId,
            tags: tags,
            metadata: meta,
            ct: ct);
    }

    public async Task ApplySuggestionAsync(Guid documentId, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var dto = await _queries.GetAsync(tenantId, documentId, ct);
        if (dto?.SuggestedTypeId is null)
            throw new InvalidOperationException("Não há sugestão para aplicar.");

        await _commands.ApplySuggestionAsync(
            tenantId: tenantId,
            documentId: documentId,
            suggestedTypeId: dto.SuggestedTypeId.Value,
            suggestedConfidence: dto.SuggestedConfidence,
            suggestedSummary: dto.SuggestedSummary,
            userId: userId,
            ct: ct);
    }

    // ✅ compatível com IOcrAutoClassificationService
    public async Task<DocumentTypeSuggestionDto?> SuggestByLatestVersionTextAsync(Guid documentId, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var dto = await _queries.GetAsync(tenantId, documentId, ct);
        if (dto is null) return null;

        var text = await _searchText.GetOcrTextAsync(tenantId, documentId, ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogInformation("Sem OCR text para sugestão. Tenant={Tenant} Doc={Doc}", tenantId, documentId);
            return new DocumentTypeSuggestionDto(null, null, null, "Sem texto OCR indexado para sugerir.");
        }

        var types = await _queries.ListTypesAsync(tenantId, ct);
        if (types is null || types.Count == 0)
            return new DocumentTypeSuggestionDto(null, null, null, "Nenhum tipo de documento cadastrado.");

        // ✅ sua assinatura real: Suggest(string?, IEnumerable<(Guid id, string name)>)
        var suggestion = _suggester.Suggest(text, types.Select(t => (t.Id, t.Name)));

        if (suggestion is null)
            return new DocumentTypeSuggestionDto(null, null, null, "Não foi possível sugerir um tipo.");

        // ✅ CORREÇÃO: Suggestion não tem TypeId — usa Deconstruct
        suggestion.Deconstruct(out Guid? typeId, out decimal? confidence, out string? typeName, out string? summary);

        if (typeId is null)
            return new DocumentTypeSuggestionDto(null, null, null, "Não foi possível sugerir um tipo.");

        await _commands.SaveSuggestionOnlyAsync(
            tenantId: tenantId,
            documentId: documentId,
            suggestedTypeId: typeId.Value,
            suggestedConfidence: confidence,
            suggestedSummary: summary,
            ct: ct);

        return new DocumentTypeSuggestionDto(typeId, typeName, confidence, summary);
    }

    private static List<string> ParseTags(string? tagsCsv)
    {
        if (string.IsNullOrWhiteSpace(tagsCsv)) return new();

        return tagsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> ParseMetadata(string? metadataLines)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(metadataLines)) return dict;

        foreach (var lineRaw in metadataLines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = lineRaw.Trim();
            if (line.Length == 0) continue;

            var idx = line.IndexOf('=');
            if (idx <= 0) continue;

            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(key)) continue;
            dict[key] = val;
        }

        return dict;
    }
}

public interface ICurrentUserAccessor
{
    Guid TenantId { get; }
    Guid? UserId { get; }
}
