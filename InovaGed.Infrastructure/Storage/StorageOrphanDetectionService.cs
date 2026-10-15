using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Storage;

public sealed class StorageOrphanDetectionService : IStorageOrphanDetector
{
    private readonly IDbConnectionFactory _db;
    private readonly LocalStorageOptions _storageOptions;
    private readonly ILogger<StorageOrphanDetectionService> _logger;

    public StorageOrphanDetectionService(
        IDbConnectionFactory db,
        IOptions<LocalStorageOptions> storageOptions,
        ILogger<StorageOrphanDetectionService> logger)
    {
        _db = db;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public async Task<StorageOrphanReport> ScanOrphansAsync(Guid? tenantId, TimeSpan gracePeriod, CancellationToken ct)
    {
        if (gracePeriod <= TimeSpan.Zero)
            gracePeriod = TimeSpan.FromHours(24);

        var rootDir = Path.GetFullPath(_storageOptions.RootPath);
        if (!Directory.Exists(rootDir))
        {
            return new StorageOrphanReport(
                DateTimeOffset.UtcNow,
                gracePeriod,
                0, 0, 0, 0, 0,
                Array.Empty<OrphanFileInfo>());
        }

        await using var conn = await _db.OpenAsync(ct);

        // 1. Obter todos os storage_paths de versões confirmadas
        var versionPaths = (await conn.QueryAsync<string>(new CommandDefinition("""
select storage_path from ged.document_version
where (@tenantId is null or tenant_id = @tenantId) and storage_path is not null
""", new { tenantId }, cancellationToken: ct)))
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2. Obter storage_paths de prévias geradas (se tabela existir no schema)
        var hasPreviewTable = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select to_regclass('ged.document_preview') is not null", cancellationToken: ct));
        var previewPaths = hasPreviewTable
            ? (await conn.QueryAsync<string>(new CommandDefinition("""
select preview_path from ged.document_preview
where (@tenantId is null or tenant_id = @tenantId) and preview_path is not null
""", new { tenantId }, cancellationToken: ct)))
                .Select(NormalizePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 3. Obter identificadores de sessões ativas de upload recentes (protegidas pelo período de carência)
        var activeUploads = (await conn.QueryAsync<string>(new CommandDefinition("""
select id::text from ged.upload_session
where (@tenantId is null or tenant_id = @tenantId)
  and (status in ('ACTIVE', 'PENDING', 'UPLOADING') or updated_at > now() - @graceInterval)
""", new { tenantId, graceInterval = gracePeriod }, cancellationToken: ct)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<OrphanFileInfo>();
        var totalScanned = 0;
        var confirmedReferenced = 0;
        var activeUploadFiles = 0;
        long orphanBytesTotal = 0;
        var cutoff = DateTimeOffset.UtcNow - gracePeriod;

        var allFiles = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories);

        foreach (var fullPath in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            totalScanned++;

            var relative = Path.GetRelativePath(rootDir, fullPath);
            var normalizedRel = NormalizePath(relative);

            // Verificar se pertence a versão confirmada
            if (versionPaths.Contains(normalizedRel) || previewPaths.Contains(normalizedRel))
            {
                confirmedReferenced++;
                continue;
            }

            // Verificar se o arquivo está sob diretório de upload ativo
            var segments = normalizedRel.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            var isInActiveUpload = segments.Any(s => activeUploads.Contains(s));
            if (isInActiveUpload)
            {
                activeUploadFiles++;
                continue;
            }

            // Verificar tempo de modificação em relação ao período de segurança
            var fileInfo = new FileInfo(fullPath);
            var lastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

            if (lastModified >= cutoff)
            {
                // Arquivo recente protegido pelo período de segurança
                activeUploadFiles++;
                continue;
            }

            // Arquivo não referenciado e fora do período de segurança -> candidato a órfão
            orphanBytesTotal += fileInfo.Length;
            candidates.Add(new OrphanFileInfo(
                relative,
                fileInfo.Length,
                lastModified,
                "Arquivo físico sem referência em ged.document_version nem prévias ativas, fora do período de segurança."));
        }

        _logger.LogInformation(
            "Varredura de órfãos de armazenamento concluída. Total: {Total}, Referenciados: {Referenced}, Candidatos: {Candidates}, Bytes: {Bytes}",
            totalScanned, confirmedReferenced, candidates.Count, orphanBytesTotal);

        return new StorageOrphanReport(
            DateTimeOffset.UtcNow,
            gracePeriod,
            totalScanned,
            confirmedReferenced,
            activeUploadFiles,
            candidates.Count,
            orphanBytesTotal,
            candidates);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}
