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

        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        // Determinar o escopo físico a ser percorrido
        string scanDir = rootDir;
        bool isDedicatedTenantDir = false;

        if (tenantId.HasValue)
        {
            var possibleTenantDirs = new[]
            {
                Path.Combine(rootDir, tenantId.Value.ToString()),
                Path.Combine(rootDir, tenantId.Value.ToString("D")),
                Path.Combine(rootDir, tenantId.Value.ToString("N"))
            };

            foreach (var td in possibleTenantDirs)
            {
                if (Directory.Exists(td))
                {
                    scanDir = td;
                    isDedicatedTenantDir = true;
                    break;
                }
            }
        }

        await using var conn = await _db.OpenAsync(ct);

        // 1. Obter referências de versões de documentos (sempre carregar globais para evitar classificar arquivos de outros tenants como órfãos)
        var allVersionPaths = (await conn.QueryAsync<string>(new CommandDefinition("""
select storage_path from ged.document_version where storage_path is not null
""", cancellationToken: ct)))
            .Select(NormalizePath)
            .ToHashSet(pathComparer);

        // 2. Obter referências de prévias geradas
        var hasPreviewTable = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select to_regclass('ged.document_preview') is not null", cancellationToken: ct));
        var allPreviewPaths = hasPreviewTable
            ? (await conn.QueryAsync<string>(new CommandDefinition("""
select preview_path from ged.document_preview where preview_path is not null
""", cancellationToken: ct)))
                .Select(NormalizePath)
                .ToHashSet(pathComparer)
            : new HashSet<string>(pathComparer);

        // 3. Obter anexos de protocolo, evidências de etiqueta e jobs de OCR se existirem
        var hasProtocolAttachment = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select to_regclass('ged.protocol_request_attachment') is not null", cancellationToken: ct));
        var protocolPaths = hasProtocolAttachment
            ? (await conn.QueryAsync<string>(new CommandDefinition("""
select storage_path from ged.protocol_request_attachment where storage_path is not null
""", cancellationToken: ct)))
                .Select(NormalizePath)
                .ToHashSet(pathComparer)
            : new HashSet<string>(pathComparer);

        var hasLabelEvidence = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select to_regclass('ged.label_evidence') is not null", cancellationToken: ct));
        var labelPaths = hasLabelEvidence
            ? (await conn.QueryAsync<string>(new CommandDefinition("""
select storage_path from ged.label_evidence where storage_path is not null
""", cancellationToken: ct)))
                .Select(NormalizePath)
                .ToHashSet(pathComparer)
            : new HashSet<string>(pathComparer);

        // 4. Identificadores de sessões ativas de upload recentes
        var activeUploads = (await conn.QueryAsync<string>(new CommandDefinition("""
select id::text from ged.upload_session
where status in ('ACTIVE', 'PENDING', 'UPLOADING') or updated_at > now() - @graceInterval
""", new { graceInterval = gracePeriod }, cancellationToken: ct)))
            .ToHashSet(pathComparer);

        // 5. Todos os outros tenants cadastrados (para garantir isolamento de caminhos e nunca reportar ou vazar arquivos de outro tenant)
        var otherTenantGuids = (await conn.QueryAsync<Guid>(new CommandDefinition("""
select id from ged.tenant where @tenantId is null or id <> @tenantId
""", new { tenantId }, cancellationToken: ct)))
            .Select(g => g.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<OrphanFileInfo>();
        var totalScanned = 0;
        var confirmedReferenced = 0;
        var activeUploadFiles = 0;
        long orphanBytesTotal = 0;
        var cutoff = DateTimeOffset.UtcNow - gracePeriod;

        IEnumerable<string> fileEnumerable;
        try
        {
            fileEnumerable = Directory.EnumerateFiles(scanDir, "*", SearchOption.AllDirectories);
        }
        catch (DirectoryNotFoundException)
        {
            fileEnumerable = Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enumerar arquivos no diretório de armazenamento: {Directory}", scanDir);
            fileEnumerable = Array.Empty<string>();
        }

        foreach (var fullPath in fileEnumerable)
        {
            ct.ThrowIfCancellationRequested();

            FileInfo fileInfo;
            try
            {
                var resolvedFull = Path.GetFullPath(fullPath);
                // Proteção contra links simbólicos e path traversal saindo da raiz
                if (!resolvedFull.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Arquivo ignorado por escapar da raiz configurada: {Path}", resolvedFull);
                    continue;
                }

                fileInfo = new FileInfo(resolvedFull);
                if (!fileInfo.Exists)
                    continue;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
            {
                continue;
            }

            totalScanned++;

            var relativeFromRoot = Path.GetRelativePath(rootDir, fullPath);
            var normalizedRelRoot = NormalizePath(relativeFromRoot);

            var relativeFromScan = Path.GetRelativePath(scanDir, fullPath);
            var normalizedRelScan = NormalizePath(relativeFromScan);

            // Verificar se pertence a versões confirmadas globais, prévias, protocolos ou evidências
            if (allVersionPaths.Contains(normalizedRelRoot) || allVersionPaths.Contains(normalizedRelScan) ||
                allPreviewPaths.Contains(normalizedRelRoot) || allPreviewPaths.Contains(normalizedRelScan) ||
                protocolPaths.Contains(normalizedRelRoot) || protocolPaths.Contains(normalizedRelScan) ||
                labelPaths.Contains(normalizedRelRoot) || labelPaths.Contains(normalizedRelScan))
            {
                confirmedReferenced++;
                continue;
            }

            // Se a busca for de um tenant específico e não estiver em pasta exclusiva,
            // verificar se o arquivo pertence ou está na estrutura de outro tenant
            if (tenantId.HasValue && !isDedicatedTenantDir)
            {
                var segments = normalizedRelRoot.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var belongsToOtherTenant = segments.Any(s => otherTenantGuids.Contains(s));
                if (belongsToOtherTenant)
                {
                    // Não pertence a este tenant nem pode ser revelado neste relatório
                    continue;
                }
            }

            // Verificar se o arquivo está sob diretório de upload ativo
            var pathSegments = normalizedRelRoot.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var isInActiveUpload = pathSegments.Any(s => activeUploads.Contains(s));
            if (isInActiveUpload)
            {
                activeUploadFiles++;
                continue;
            }

            // Verificar tempo de modificação em relação ao período de segurança
            DateTimeOffset lastModified;
            long fileLength;
            try
            {
                lastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
                fileLength = fileInfo.Length;
            }
            catch (Exception)
            {
                // Arquivo pode ter sido modificado ou removido concorrentemente
                continue;
            }

            if (lastModified >= cutoff)
            {
                // Arquivo recente protegido pelo período de segurança
                activeUploadFiles++;
                continue;
            }

            // Arquivo não referenciado em nenhuma base e fora do período de segurança -> candidato a órfão
            orphanBytesTotal += fileLength;
            candidates.Add(new OrphanFileInfo(
                relativeFromRoot,
                fileLength,
                lastModified,
                "Arquivo físico sem referência global em ged.document_version, prévias ou uploads ativos, fora do período de segurança."));
        }

        _logger.LogInformation(
            "Varredura de órfãos de armazenamento concluída para tenant {TenantId}. Total: {Total}, Referenciados: {Referenced}, Candidatos: {Candidates}, Bytes: {Bytes}",
            tenantId, totalScanned, confirmedReferenced, candidates.Count, orphanBytesTotal);

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
