using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using InovaGed.Application.Auditing;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Identity;
using InovaGed.Domain.Documents;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Application.Documents;

public sealed class DocumentAppService
{
    private readonly ICurrentUser _currentUser;
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentWriteRepository _writeRepo;
    private readonly IPreviewGenerator _preview;
    private readonly IPdfTextExtractor _ocr;
    private readonly IAuditLogWriter _audit;
    private readonly IFileStorage _storage;
    private readonly ILogger<DocumentAppService> _logger;

    public DocumentAppService(
        ICurrentUser currentUser,
        IDbConnectionFactory db,
        IPreviewGenerator preview,
        IPdfTextExtractor ocr,
        IDocumentWriteRepository writeRepo,
        IAuditLogWriter audit,
        IFileStorage storage,
        ILogger<DocumentAppService> logger)
    {
        _currentUser = currentUser;
        _db = db;
        _preview = preview;
        _ocr = ocr;
        _writeRepo = writeRepo;
        _audit = audit;
        _storage = storage;
        _logger = logger;
    }

    // ==========================================================
    // UPLOAD (cria documento + versão 1)
    // ==========================================================
    public async Task<Result<Guid>> UploadAsync(
        UploadDocumentCommand cmd,
        string ip,
        string userAgent,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid>.Fail("AUTH", "Usuário não autenticado.");

        if (cmd.Content is null || cmd.Content == Stream.Null)
            return Result<Guid>.Fail("FILE", "Arquivo inválido.");

        if (cmd.FolderId == Guid.Empty)
            return Result<Guid>.Fail("VALIDATION", "Pasta inválida.");

        if (string.IsNullOrWhiteSpace(cmd.Title))
            return Result<Guid>.Fail("VALIDATION", "Título é obrigatório.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        var fileName = string.IsNullOrWhiteSpace(cmd.FileName) ? "arquivo" : cmd.FileName.Trim();
        var ext = Path.GetExtension(fileName);
        var fileExtension = ext.StartsWith(".") ? ext[1..] : ext;

        var contentType = string.IsNullOrWhiteSpace(cmd.ContentType)
            ? "application/octet-stream"
            : cmd.ContentType.Trim();

        long sizeBytes = 0;
        if (cmd.Content.CanSeek)
        {
            sizeBytes = cmd.Content.Length;
            cmd.Content.Position = 0;
        }

        var visibility = NormalizeVisibility(cmd.Visibility);

        var isConfidential = cmd.IsConfidential ?? false;
        if (isConfidential)
            visibility = "PRIVATE";

        static string NewCode()
        {
            var suffix = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
            var code = $"DOC-{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}";
            return code.Length <= 50 ? code : code[..50];
        }

        var code = NewCode();

        try
        {
            using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // 1) insert DOCUMENT (metadados)
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await _writeRepo.InsertDocumentAsync(new DocumentRow
                    {
                        Id = documentId,
                        TenantId = tenantId,
                        Code = code,
                        Title = cmd.Title.Trim(),
                        Description = cmd.Description,
                        FolderId = cmd.FolderId,
                        DepartmentId = cmd.DepartmentId,
                        TypeId = cmd.TypeId,
                        ClassificationId = cmd.ClassificationId,
                        Status = "DRAFT",
                        Visibility = visibility,
                        CurrentVersionId = versionId,
                        CreatedBy = userId,
                        IsConfidential = isConfidential
                    }, tx, ct);

                    break;
                }
                catch (PostgresException pg) when (pg.SqlState == "23505" &&
                                                  (pg.ConstraintName ?? "")
                                                  .Equals("ux_document_tenant_code", StringComparison.OrdinalIgnoreCase))
                {
                    code = NewCode();
                    if (attempt == 1) throw;
                }
            }

            // 2) storage
            if (cmd.Content.CanSeek) cmd.Content.Position = 0;

            var (storagePath, realSizeBytes, md5, sha256) = await _storage.SaveAsync(
                cmd.Content,
                fileName,
                contentType,
                tenantId,
                documentId,
                versionId,
                ct);

            // 3) insert VERSION
            await _writeRepo.InsertVersionAsync(new DocumentVersionRow
            {
                Id = versionId,
                TenantId = tenantId,
                DocumentId = documentId,
                VersionNumber = 1,
                FileName = fileName,
                FileExtension = fileExtension,
                FileSizeBytes = (realSizeBytes > 0 ? realSizeBytes : sizeBytes),
                StoragePath = storagePath,
                ChecksumMd5 = md5,
                ChecksumSha256 = sha256,
                ContentType = contentType,
                CreatedBy = userId
            }, tx, ct);

            // 4) current_version_id
            await _writeRepo.UpdateCurrentVersionAsync(tenantId, documentId, versionId, userId, tx, ct);

            // 4.5) SEARCH INDEX (metadados + OCR vazio no upload)
            await UpsertSearchAsync(
                tx,
                tenantId: tenantId,
                documentId: documentId,
                versionId: versionId,
                title: cmd.Title.Trim(),
                description: cmd.Description ?? "",
                code: code,
                fileName: fileName,
                ocrText: "", // upload normal ainda sem OCR
                ct: ct);

            // 5) auditoria (não bloqueia)
            try
            {
                await _audit.InsertAsync(new AuditLogRow
                {
                    TenantId = tenantId,
                    EntityName = "document",
                    EntityId = documentId,
                    Action = "UPLOAD",
                    UserId = userId,
                    DetailsJson = $"{{\"file\":\"{EscapeJson(fileName)}\",\"ip\":\"{EscapeJson(ip)}\",\"ua\":\"{EscapeJson(userAgent)}\"}}"
                }, tx, ct);
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(auditEx, "Falha ao gravar audit_log (não bloqueia upload).");
            }

            tx.Commit();
            return Result<Guid>.Ok(documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no upload do documento.");
            return Result<Guid>.Fail("ERR", "Erro ao realizar upload.");
        }
    }

    // ==========================================================
    // LEGADO (mantém como está)
    // ==========================================================
    public async Task InsertDocumentWithVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid? folderId,
        Guid? typeId,
        string title,
        string? description,
        bool isConfidential,
        Guid versionId,
        string fileName,
        string fileExtension,
        long fileSizeBytes,
        string contentType,
        string storagePath,
        Guid? createdBy,
        CancellationToken ct)
    {
        try
        {
            using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            var code = $"DOC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".ToUpperInvariant();
            var visibility = isConfidential ? "PRIVATE" : "INTERNAL";

            await _writeRepo.InsertDocumentAsync(new DocumentRow
            {
                Id = documentId,
                TenantId = tenantId,
                Code = code,
                Title = title.Trim(),
                Description = description,
                FolderId = folderId,
                TypeId = typeId,
                Status = "DRAFT",
                Visibility = visibility,
                IsConfidential = isConfidential,
                CurrentVersionId = versionId,
                CreatedBy = createdBy
            }, tx, ct);

            await _writeRepo.InsertVersionAsync(new DocumentVersionRow
            {
                Id = versionId,
                TenantId = tenantId,
                DocumentId = documentId,
                VersionNumber = 1,
                FileName = fileName,
                FileExtension = fileExtension,
                FileSizeBytes = fileSizeBytes,
                StoragePath = storagePath,
                ContentType = contentType,
                CreatedBy = createdBy
            }, tx, ct);

            await _writeRepo.UpdateCurrentVersionAsync(tenantId, documentId, versionId, createdBy, tx, ct);

            // search index (opcional) - aqui você pode indexar também se quiser:
            // await UpsertSearchAsync(tx, tenantId, documentId, versionId, title, description ?? "", code, fileName, "", ct);

            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no InsertDocumentWithVersionAsync. Doc={DocId}", documentId);
            throw;
        }
    }

    // ==========================================================
    // Preview + OCR (mantido; usado se você quiser processamento assíncrono)
    // ==========================================================
    public async Task ProcessPreviewAndOcrAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string storagePath,
        string fileName,
        CancellationToken ct)
    {
        var previewPath = await _preview.GetOrCreatePreviewPdfAsync(
            tenantId,
            documentId,
            versionId,
            storagePath,
            fileName,
            ct);

        var extractedText = await _ocr.ExtractTextAsync(previewPath, ct);

        // Se quiser indexar aqui também (quando esse fluxo for usado),
        // você precisará buscar title/description/code do document e chamar UpsertSearchAsync com extractedText.
        // Mantive neutro para não mudar fluxo atual.
        _logger.LogInformation("ProcessPreviewAndOcrAsync concluído. Doc={DocId}, Ver={VerId}, HasText={HasText}",
            documentId, versionId, !string.IsNullOrWhiteSpace(extractedText));
    }

    // ==========================================================
    // ADD VERSION (compatível) - sem OCR
    // ==========================================================
    public Task<Result<Guid>> AddVersionAsync(
        Guid documentId,
        Stream content,
        string fileName,
        string contentType,
        string ip,
        string userAgent,
        CancellationToken ct)
    {
        return AddVersionInternalAsync(
            documentId,
            content,
            fileName,
            contentType,
            ocrText: "",
            ip,
            userAgent,
            ct);
    }

    // ==========================================================
    // ADD VERSION (OCR) - para o RunOcr passar extractedText real
    // ==========================================================
    public Task<Result<Guid>> AddVersionWithOcrAsync(
        Guid documentId,
        Stream content,
        string fileName,
        string contentType,
        string ocrText,
        string ip,
        string userAgent,
        CancellationToken ct)
    {
        return AddVersionInternalAsync(
            documentId,
            content,
            fileName,
            contentType,
            ocrText: ocrText ?? "",
            ip,
            userAgent,
            ct);
    }

    // ==========================================================
    // IMPLEMENTAÇÃO ÚNICA
    // ==========================================================
    private async Task<Result<Guid>> AddVersionInternalAsync(
        Guid documentId,
        Stream content,
        string fileName,
        string contentType,
        string ocrText,
        string ip,
        string userAgent,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid>.Fail("AUTH", "Usuário não autenticado.");

        if (documentId == Guid.Empty)
            return Result<Guid>.Fail("VALIDATION", "Documento inválido.");

        if (content is null || content == Stream.Null)
            return Result<Guid>.Fail("FILE", "Arquivo inválido.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var versionId = Guid.NewGuid();

        var ext = Path.GetExtension(fileName ?? "");
        var fileExtension = string.IsNullOrWhiteSpace(ext) ? "" : (ext.StartsWith(".") ? ext[1..] : ext);

        try
        {
            using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // 1) próximo número
            var nextVersion = await _writeRepo.GetNextVersionNumberAsync(tenantId, documentId, tx, ct);

            // 2) storage
            if (content.CanSeek) content.Position = 0;

            var (storagePath, realSizeBytes, md5, sha256) = await _storage.SaveAsync(
                content,
                fileName,
                contentType,
                tenantId,
                documentId,
                versionId,
                ct);

            // 3) insert version
            await _writeRepo.InsertVersionAsync(new DocumentVersionRow
            {
                Id = versionId,
                TenantId = tenantId,
                DocumentId = documentId,
                VersionNumber = nextVersion,
                FileName = fileName.Trim(),
                FileExtension = fileExtension,
                FileSizeBytes = realSizeBytes,
                StoragePath = storagePath,
                ChecksumMd5 = md5,
                ChecksumSha256 = sha256,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
                CreatedBy = userId
            }, tx, ct);

            // 4) current_version_id
            await _writeRepo.UpdateCurrentVersionAsync(tenantId, documentId, versionId, userId, tx, ct);

            // 4.5) SEARCH INDEX (metadados + OCR REAL quando vier)
            const string sqlDoc = @"
SELECT 
  code        AS ""Code"",
  title       AS ""Title"",
  COALESCE(description,'') AS ""Description""
FROM ged.document
WHERE tenant_id = @tenantId
  AND id = @documentId;
";
            var meta = await tx.Connection!.QuerySingleAsync<(string Code, string Title, string Description)>(
                new CommandDefinition(sqlDoc, new { tenantId, documentId }, tx, cancellationToken: ct));

            await UpsertSearchAsync(
                tx,
                tenantId: tenantId,
                documentId: documentId,
                versionId: versionId,
                title: meta.Title,
                description: meta.Description,
                code: meta.Code,
                fileName: fileName.Trim(),
                ocrText: ocrText ?? "",
                ct: ct);

            // 5) auditoria (não bloqueia)
            try
            {
                await _audit.InsertAsync(new AuditLogRow
                {
                    TenantId = tenantId,
                    EntityName = "document",
                    EntityId = documentId,
                    Action = "ADD_VERSION", // atenção: precisa existir no enum do banco
                    UserId = userId,
                    DetailsJson = $"{{\"file\":\"{EscapeJson(fileName)}\",\"ip\":\"{EscapeJson(ip)}\",\"ua\":\"{EscapeJson(userAgent)}\"}}"
                }, tx, ct);
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(auditEx, "Falha ao gravar audit_log (não bloqueia AddVersion).");
            }

            tx.Commit();
            return Result<Guid>.Ok(versionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao adicionar nova versão. Doc={DocId}", documentId);
            return Result<Guid>.Fail("ERR", "Erro ao adicionar nova versão.");
        }
    }

    // ==========================================================
    // Helpers
    // ==========================================================
    private static string NormalizeVisibility(string? value)
    {
        var v = (value ?? "INTERNAL").Trim().ToUpperInvariant();
        return v switch
        {
            "PRIVATE" => "PRIVATE",
            "PUBLIC" => "PUBLIC",
            _ => "INTERNAL"
        };
    }

    private static string EscapeJson(string? s)
        => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static async Task UpsertSearchAsync(
        IDbTransaction tx,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string title,
        string description,
        string code,
        string fileName,
        string ocrText,
        CancellationToken ct)
    {
        // Ordem correta: code, title, description, fileName, ocrText
        const string sql = @"
SELECT ged.upsert_document_search(
  @tenantId, @documentId, @versionId,
  @code, @title, @description, @fileName, @ocrText
);";

        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            sql,
            new { tenantId, documentId, versionId, code, title, description, fileName, ocrText },
            tx,
            cancellationToken: ct));
    }
}
