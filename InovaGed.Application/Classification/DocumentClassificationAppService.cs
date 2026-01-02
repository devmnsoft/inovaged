using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;

namespace InovaGed.Application.Classification;

public sealed class DocumentClassificationAppService
{
    private readonly ICurrentUser _currentUser;
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentClassifier _classifier;
    private readonly IDocumentClassificationRepository _repo;
    private readonly IFolderClassificationRuleRepository _folderRuleRepo;
    private readonly IDocumentTypeCatalogQueries _types;
    private readonly SimpleTextDocumentTypeSuggester _suggester;

    public DocumentClassificationAppService(
        ICurrentUser currentUser,
        IDbConnectionFactory db,
        IDocumentClassifier classifier,
        IDocumentClassificationRepository repo,
        IFolderClassificationRuleRepository folderRuleRepo,
        IDocumentTypeCatalogQueries types,
        SimpleTextDocumentTypeSuggester suggester)
    {
        _currentUser = currentUser;
        _db = db;
        _classifier = classifier;
        _repo = repo;
        _folderRuleRepo = folderRuleRepo;
        _types = types;
        _suggester = suggester;
    }

    // ======= EXISTENTE (mantenho) =======
    public async Task ReclassifyLatestVersionAsync(Guid documentId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            throw new InvalidOperationException("Usuário não autenticado.");

        const string sql = @"
SELECT
  v.id AS VersionId,
  COALESCE(NULLIF(v.ocr_text, ''), NULLIF(v.content_text, ''), '') AS TextForClassification
FROM ged.document_version v
WHERE v.tenant_id = @tenantId
  AND v.document_id = @documentId
ORDER BY v.created_at DESC
LIMIT 1;
";
        var conn = await _db.OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<LatestVersionRow>(
            new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, documentId }, cancellationToken: ct));

        if (row is null)
            throw new InvalidOperationException("Documento não possui versões.");

        var classified = await _classifier.ClassifyAsync(
            tenantId: _currentUser.TenantId,
            documentId: documentId,
            documentVersionId: row.VersionId,
            ocrText: row.TextForClassification ?? "",
            ct: ct);

        await _repo.UpsertClassificationAsync(
            tenantId: _currentUser.TenantId,
            documentId: documentId,
            documentVersionId: row.VersionId,
            documentTypeId: classified.DocumentTypeId,
            confidence: classified.Confidence,
            method: "MANUAL",
            summary: classified.Summary,
            classifiedBy: _currentUser.UserId,
            ct: ct);

        await _repo.ReplaceTagsAsync(_currentUser.TenantId, documentId, classified.Tags, "MANUAL", _currentUser.UserId, ct);
        await _repo.ReplaceMetadataAsync(_currentUser.TenantId, documentId, classified.Metadata, "MANUAL", ct);
    }

    // ======= EXISTENTE (mantenho) =======
    public async Task SaveManualAsync(Guid documentId, Guid? documentTypeId, string? tagsCsv, string? metadataLines, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            throw new InvalidOperationException("Usuário não autenticado.");

        const string sql = @"
SELECT v.id AS VersionId
FROM ged.document_version v
WHERE v.tenant_id = @tenantId
  AND v.document_id = @documentId
ORDER BY v.created_at DESC
LIMIT 1;
";
        var conn = await _db.OpenAsync(ct);
        var versionId = await conn.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, documentId }, cancellationToken: ct));

        if (versionId is null)
            throw new InvalidOperationException("Documento não possui versões.");

        var tags = ParseTags(tagsCsv);
        var meta = ParseMetadata(metadataLines);

        await _repo.UpsertClassificationAsync(
            tenantId: _currentUser.TenantId,
            documentId: documentId,
            documentVersionId: versionId.Value,
            documentTypeId: documentTypeId,
            confidence: null,
            method: "MANUAL",
            summary: "Ajustado manualmente pelo usuário.",
            classifiedBy: _currentUser.UserId,
            ct: ct);

        await _repo.ReplaceTagsAsync(_currentUser.TenantId, documentId, tags, "MANUAL", _currentUser.UserId, ct);
        await _repo.ReplaceMetadataAsync(_currentUser.TenantId, documentId, meta, "MANUAL", ct);
    }

    // ============================================================
    // ✅ 1) AUTO CLASSIFICAÇÃO POR PASTA (tipo padrão da pasta)
    // ============================================================
    public async Task AutoClassifyByFolderAsync(Guid documentId, Guid folderId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            throw new InvalidOperationException("Usuário não autenticado.");

        var (versionId, currentTypeId) = await GetLatestVersionAndCurrentTypeAsync(documentId, ct);

        if (currentTypeId != null) return;

        var defaultTypeId = await _folderRuleRepo.GetDefaultTypeAsync(_currentUser.TenantId, folderId, ct);
        if (defaultTypeId == null) return;

        await _repo.UpsertClassificationAsync(
            tenantId: _currentUser.TenantId,
            documentId: documentId,
            documentVersionId: versionId,
            documentTypeId: defaultTypeId,
            confidence: null,
            method: "AUTO_FOLDER",
            summary: "Classificação automática baseada no tipo padrão da pasta.",
            classifiedBy: _currentUser.UserId,
            ct: ct);
    }

    // ============================================================
    // ✅ 2) SUGESTÃO POR OCR/TEXTO (última versão)
    // ============================================================
    public async Task SuggestByLatestVersionTextAsync(Guid documentId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            throw new InvalidOperationException("Usuário não autenticado.");

        const string sql = @"
SELECT
  v.id AS VersionId,
  COALESCE(NULLIF(v.ocr_text, ''), NULLIF(v.content_text, ''), '') AS TextForClassification
FROM ged.document_version v
WHERE v.tenant_id=@tenantId AND v.document_id=@documentId
ORDER BY v.created_at DESC
LIMIT 1;";
        var conn = await _db.OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<LatestVersionRow>(
            new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, documentId }, cancellationToken: ct));

        if (row is null) return;

        // ✅ carrega catálogo de tipos
        var types = await _types.ListAsync(_currentUser.TenantId, ct);
        var tuples = types.Select(t => (t.Id, t.Name)).ToList();

        // ✅ chama o suggester no NOVO formato
        var sug = _suggester.Suggest(row.TextForClassification, tuples);

        // se não sugeriu nada, limpa sugestão
        if (sug.SuggestedTypeId is null)
        {
            await _repo.SetSuggestionAsync(
                _currentUser.TenantId,
                documentId,
                null,
                null,
                "Nenhuma sugestão identificada por OCR/texto.",
                DateTimeOffset.UtcNow,
                ct);

            return;
        }

        // salva sugestão
        await _repo.SetSuggestionAsync(
            _currentUser.TenantId,
            documentId,
            sug.SuggestedTypeId,
            sug.Confidence,
            sug.Summary ?? sug.Method ?? "TEXT_RULES",
            DateTimeOffset.UtcNow,
            ct);

        // se confiança alta e ainda não classificado, aplica automaticamente
        var has = await _repo.HasClassificationAsync(_currentUser.TenantId, documentId, ct);

        if (!has && (sug.Confidence ?? 0m) >= 0.80m)
        {
            await _repo.UpsertClassificationAsync(
                tenantId: _currentUser.TenantId,
                documentId: documentId,
                documentVersionId: row.VersionId,
                documentTypeId: sug.SuggestedTypeId,
                confidence: sug.Confidence,
                method: "AUTO_OCR",
                summary: sug.Summary ?? "Classificação automática por OCR/texto.",
                classifiedBy: _currentUser.UserId,
                ct: ct);
        }
    }

    // ============================================================
    // ✅ 2b) APLICAR SUGESTÃO (botão)
    // ============================================================
    public async Task ApplySuggestionAsync(Guid documentId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            throw new InvalidOperationException("Usuário não autenticado.");

        const string sql = @"
SELECT suggested_type_id
FROM ged.document_classification
WHERE tenant_id=@tenantId AND document_id=@documentId
LIMIT 1;";
        var conn = await _db.OpenAsync(ct);

        var suggestedTypeId = await conn.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, documentId }, cancellationToken: ct));

        if (suggestedTypeId is null) return;

        var (versionId, currentTypeId) = await GetLatestVersionAndCurrentTypeAsync(documentId, ct);
        if (currentTypeId != null) return;

        await _repo.UpsertClassificationAsync(
            tenantId: _currentUser.TenantId,
            documentId: documentId,
            documentVersionId: versionId,
            documentTypeId: suggestedTypeId,
            confidence: null,
            method: "AUTO_OCR",
            summary: "Sugestão aplicada manualmente pelo usuário.",
            classifiedBy: _currentUser.UserId,
            ct: ct);
    }

    // ===== Helpers =====
    private async Task<(Guid VersionId, Guid? CurrentTypeId)> GetLatestVersionAndCurrentTypeAsync(Guid documentId, CancellationToken ct)
    {
        const string sql = @"
WITH lastv AS (
  SELECT v.id AS version_id
  FROM ged.document_version v
  WHERE v.tenant_id=@tenantId AND v.document_id=@documentId
  ORDER BY v.created_at DESC
  LIMIT 1
)
SELECT
  lastv.version_id AS VersionId,
  c.document_type_id AS CurrentTypeId
FROM lastv
LEFT JOIN ged.document_classification c
  ON c.tenant_id=@tenantId AND c.document_id=@documentId;";
        var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleAsync<(Guid VersionId, Guid? CurrentTypeId)>(
            new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, documentId }, cancellationToken: ct));
    }

    private static List<string> ParseTags(string? csv)
        => (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Dictionary<string, (string Value, decimal? Confidence)> ParseMetadata(string? lines)
    {
        var dict = new Dictionary<string, (string Value, decimal? Confidence)>(StringComparer.OrdinalIgnoreCase);
        var text = (lines ?? "").Replace("\r\n", "\n");

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = line.Trim();
            if (s.Length == 0) continue;

            var idx = s.IndexOf('=');
            if (idx <= 0) continue;

            var key = s[..idx].Trim();
            var val = s[(idx + 1)..].Trim();
            if (key.Length == 0) continue;

            dict[key] = (val, null);
        }
        return dict;
    }

    private sealed class LatestVersionRow
    {
        public Guid VersionId { get; set; }
        public string? TextForClassification { get; set; }
    }
}
