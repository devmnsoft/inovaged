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
    private readonly IAuditLogWriter _audit;
    private readonly IFileStorage _storage;
    private readonly ILogger<DocumentAppService> _logger;

    public DocumentAppService(
        ICurrentUser currentUser,
        IDbConnectionFactory db,
        IDocumentWriteRepository writeRepo,
        IAuditLogWriter audit,
        IFileStorage storage,
        ILogger<DocumentAppService> logger)
    {
        _currentUser = currentUser;
        _db = db;
        _writeRepo = writeRepo;
        _audit = audit;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result<Guid>> UploadAsync(
        UploadDocumentCommand cmd,
        string ip,
        string userAgent,
        CancellationToken ct)
    {
        // =========================
        // validações
        // =========================
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

        // ids
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        // nome / extensão
        var fileName = string.IsNullOrWhiteSpace(cmd.FileName) ? "arquivo" : cmd.FileName.Trim();
        var ext = Path.GetExtension(fileName);
        var fileExtension = ext.StartsWith(".") ? ext[1..] : ext;

        // content-type
        var contentType = string.IsNullOrWhiteSpace(cmd.ContentType)
            ? "application/octet-stream"
            : cmd.ContentType.Trim();

        // tamanho (best-effort) + reset do stream
        long sizeBytes = 0;
        if (cmd.Content.CanSeek)
        {
            sizeBytes = cmd.Content.Length;
            cmd.Content.Position = 0;
        }

        // visibilidade (enum do banco: PRIVATE | INTERNAL | PUBLIC)
        var visibility = NormalizeVisibility(cmd.Visibility);

        // confidencial (se true, força PRIVATE por padrão)
        var isConfidential = cmd.IsConfidential ?? false;
        if (isConfidential)
            visibility = "PRIVATE";

        // code único (evita ux_document_tenant_code)
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

            // ==========================================================
            // 1) insere DOCUMENT (metadados)
            // ==========================================================
            // Retry simples: se por algum motivo raríssimo colidir code, gera outro
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

                    break; // ok
                }
                catch (PostgresException pg) when (pg.SqlState == "23505" &&
                                                  (pg.ConstraintName ?? "").Equals("ux_document_tenant_code", StringComparison.OrdinalIgnoreCase))
                {
                    code = NewCode();
                    if (attempt == 1) throw;
                }
            }

            // ==========================================================
            // 2) salva ARQUIVO no storage e recebe o storagePath REAL
            // ==========================================================
            if (cmd.Content.CanSeek) cmd.Content.Position = 0;

            var (storagePath, realSizeBytes, md5, sha256) = await _storage.SaveAsync(
                cmd.Content,
                fileName,     // ✅ aqui é o NOME ORIGINAL, NÃO o storagePath
                contentType,
                tenantId,
                documentId,
                versionId,
                ct);

            // ==========================================================
            // 3) insere VERSION (aponta para o storagePath REAL)
            // ==========================================================
            await _writeRepo.InsertVersionAsync(new DocumentVersionRow
            {
                Id = versionId,
                TenantId = tenantId,
                DocumentId = documentId,
                VersionNumber = 1,
                FileName = fileName,
                FileExtension = fileExtension,
                FileSizeBytes = (realSizeBytes > 0 ? realSizeBytes : sizeBytes),
                StoragePath = storagePath,        // ✅ ESSENCIAL
                ChecksumMd5 = md5,
                ChecksumSha256 = sha256,
                ContentType = contentType,
                CreatedBy = userId
            }, tx, ct);

            // ==========================================================
            // 4) garante current_version_id
            // ==========================================================
            await _writeRepo.UpdateCurrentVersionAsync(tenantId, documentId, versionId, userId, tx, ct);

            // ==========================================================
            // 5) auditoria (não bloqueia upload)
            // ==========================================================
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
    // LEGADO: mantém caso alguma tela antiga chame isso
    // (assume que o arquivo já foi salvo e o storagePath já está correto)
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

            // document (schema ged)
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

            // version (schema ged)
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

            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no InsertDocumentWithVersionAsync. Doc={DocId}", documentId);
            throw;
        }
    }

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
}
