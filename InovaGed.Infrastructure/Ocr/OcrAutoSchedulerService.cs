using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrAutoSchedulerService : IOcrAutoSchedulerService
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private const string SystemUserName = "Sistema - OCR automático";
    private const string Reason = "OCR automático diário - documentos sem OCR";

    private readonly IOcrAutoScheduleRepository _repository;
    private readonly IOcrJobRepository _ocrJobs;
    private readonly IDbConnectionFactory _db;
    private readonly IOptionsMonitor<OcrAutoScheduleOptions> _options;
    private readonly IOcrEnvironmentValidator _ocrEnvironmentValidator;
    private readonly ILogger<OcrAutoSchedulerService> _logger;

    public OcrAutoSchedulerService(
        IOcrAutoScheduleRepository repository,
        IOcrJobRepository ocrJobs,
        IDbConnectionFactory db,
        IOptionsMonitor<OcrAutoScheduleOptions> options,
        IOcrEnvironmentValidator ocrEnvironmentValidator,
        ILogger<OcrAutoSchedulerService> logger)
    {
        _repository = repository;
        _ocrJobs = ocrJobs;
        _db = db;
        _options = options;
        _ocrEnvironmentValidator = ocrEnvironmentValidator;
        _logger = logger;
    }

    public async Task<OcrAutoScheduleRunResultDto> RunAsync(CancellationToken ct)
    {
        var options = Normalize(_options.CurrentValue);
        var result = new OcrAutoScheduleRunResultDto
        {
            TenantId = options.TenantId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Status = "RUNNING"
        };

        if (!await Lock.WaitAsync(0, ct))
        {
            result.Status = "SKIPPED";
            result.Message = "Rotina de OCR automático já está em execução.";
            result.FinishedAtUtc = DateTimeOffset.UtcNow;
            _logger.LogWarning("OCR Auto Scheduler ignorado por execução concorrente. Tenant={TenantId} RunId={RunId}", result.TenantId, result.RunId);
            return result;
        }

        try
        {
            var env = await _ocrEnvironmentValidator.ValidateAsync(ct);
            if (!env.IsValid)
            {
                result.Status = "SKIPPED_ENVIRONMENT_INVALID";
                result.Message = "Ambiente OCR inválido. Corrija /SystemHealth/OcrEnvironment antes de executar OCR em massa. " + env.Summary;
                result.FinishedAtUtc = DateTimeOffset.UtcNow;
                await SafeInsertRunAsync(result, ct);
                await SafeUpdateRunAsync(result, ct);
                _logger.LogWarning("OCR Auto Scheduler bloqueado por ambiente inválido. {Summary}", env.Summary);
                return result;
            }

            _logger.LogInformation("OCR Auto Scheduler START. Tenant={TenantId} RunId={RunId}", result.TenantId, result.RunId);
            await SafeAuditAsync(options.TenantId, result.RunId, "OCR_AUTO_SCHEDULE_STARTED", null, null, null, result.CorrelationId, new
            {
                result.RunId,
                options.TenantId,
                result.StartedAtUtc,
                options.MaxDocumentsPerRun,
                options.BatchSize
            }, ct);

            await SafeInsertRunAsync(result, ct);

            var candidates = await _repository.GetDocumentsWithoutOcrAsync(options, ct);
            result.CandidatesFound = candidates.Count;

            foreach (var batch in candidates.Chunk(Math.Max(options.BatchSize, 1)))
            {
                foreach (var candidate in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = new OcrAutoScheduleItemResultDto
                    {
                        DocumentId = candidate.DocumentId,
                        VersionId = candidate.VersionId,
                        Title = candidate.Title,
                        FileName = candidate.FileName
                    };

                    try
                    {
                        ApplyPreEnqueueValidation(options, candidate, result, item);
                        if (item.Status.StartsWith("SKIPPED", StringComparison.OrdinalIgnoreCase))
                        {
                            await RegisterItemAsync(result, item, ct);
                            continue;
                        }

                        var latestStatus = candidate.VersionId.HasValue
                            ? await _repository.GetLatestOcrJobStatusAsync(options.TenantId, candidate.VersionId.Value, ct)
                            : null;

                        if (ShouldSkipByStatus(options, latestStatus, item, result))
                        {
                            await RegisterItemAsync(result, item, ct);
                            continue;
                        }

                        if (candidate.VersionId.HasValue && options.SkipIfOcrAvailable && await _repository.HasOcrAvailableAsync(options.TenantId, candidate.VersionId.Value, ct))
                        {
                            item.Status = "SKIPPED_ALREADY_HAS_OCR";
                            item.Reason = "OCR já disponível para a versão.";
                            result.SkippedAlreadyHasOcr++;
                            await RegisterItemAsync(result, item, ct);
                            continue;
                        }

                        var jobId = await _ocrJobs.EnqueueAsync(
                            options.TenantId,
                            candidate.VersionId!.Value,
                            options.SystemUserId,
                            invalidateDigitalSignatures: false,
                            ct);

                        item.Status = "QUEUED";
                        item.Reason = Reason;
                        item.OcrJobId = jobId.ToString();
                        result.EnqueuedCount++;
                        await RegisterItemAsync(result, item, ct);
                    }
                    catch (Exception ex)
                    {
                        item.Status = "FAILED";
                        item.Reason = ex.Message;
                        result.FailedCount++;
                        _logger.LogError(ex, "Falha ao enfileirar OCR para documento. Tenant={TenantId} DocumentId={DocumentId} VersionId={VersionId}", options.TenantId, candidate.DocumentId, candidate.VersionId);
                        await RegisterItemAsync(result, item, ct);
                    }
                }
            }

            result.Status = result.FailedCount == 0 ? "SUCCESS" : "PARTIAL_FAILURE";
            result.Message = $"Candidatos={result.CandidatesFound}; Enfileirados={result.EnqueuedCount}; Ignorados={result.SkippedCount}; Falhas={result.FailedCount}.";
            result.FinishedAtUtc = DateTimeOffset.UtcNow;
            await SafeUpdateRunAsync(result, ct);
            await SafeAuditAsync(options.TenantId, result.RunId, "OCR_AUTO_SCHEDULE_FINISHED", null, null, null, result.CorrelationId, new
            {
                result.RunId,
                result.TenantId,
                result.CandidatesFound,
                result.EnqueuedCount,
                result.SkippedCount,
                result.FailedCount,
                result.FinishedAtUtc
            }, ct);

            _logger.LogInformation("OCR Auto Scheduler FINISH. Candidates={Candidates} Enqueued={Enqueued} Skipped={Skipped} Failed={Failed}", result.CandidatesFound, result.EnqueuedCount, result.SkippedCount, result.FailedCount);
            return result;
        }
        catch (Exception ex)
        {
            result.Status = "FAILED";
            result.Message = ex.Message;
            result.FinishedAtUtc = DateTimeOffset.UtcNow;
            await SafeUpdateRunAsync(result, CancellationToken.None);
            await SafeAuditAsync(result.TenantId, result.RunId, "OCR_AUTO_SCHEDULE_FAILED", null, null, ex.Message, result.CorrelationId, new
            {
                result.RunId,
                result.TenantId,
                error = ex.Message,
                result.FinishedAtUtc
            }, CancellationToken.None);
            _logger.LogError(ex, "OCR Auto Scheduler FAILED. Tenant={TenantId} RunId={RunId}", result.TenantId, result.RunId);
            return result;
        }
        finally
        {
            Lock.Release();
        }
    }

    private static void ApplyPreEnqueueValidation(OcrAutoScheduleOptions options, OcrAutoScheduleCandidateDto candidate, OcrAutoScheduleRunResultDto result, OcrAutoScheduleItemResultDto item)
    {
        if (!candidate.VersionId.HasValue || candidate.VersionId.Value == Guid.Empty)
        {
            item.Status = "SKIPPED_NO_CURRENT_VERSION";
            item.Reason = "Documento sem versão atual/parte válida.";
            result.SkippedNoCurrentVersion++;
            return;
        }

        var extension = Path.GetExtension(candidate.FileName).ToLowerInvariant();
        var allowed = options.AllowedExtensions.Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : "." + x.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!allowed.Contains(extension))
        {
            item.Status = "SKIPPED_UNSUPPORTED_EXTENSION";
            item.Reason = $"Extensão não permitida para OCR automático: {extension}.";
            result.SkippedUnsupportedExtension++;
            return;
        }

        if (options.SkipIfOcrAvailable && candidate.HasOcrText)
        {
            item.Status = "SKIPPED_ALREADY_HAS_OCR";
            item.Reason = "OCR já disponível para a versão.";
            result.SkippedAlreadyHasOcr++;
            return;
        }
    }

    private static bool ShouldSkipByStatus(OcrAutoScheduleOptions options, string? latestStatus, OcrAutoScheduleItemResultDto item, OcrAutoScheduleRunResultDto result)
    {
        var status = (latestStatus ?? string.Empty).Trim().ToUpperInvariant();
        if (status == "PENDING" && options.SkipIfOcrJobPending)
        {
            item.Status = "SKIPPED_PENDING";
            item.Reason = "Já existe OCR pendente para a versão.";
            result.SkippedPending++;
            return true;
        }

        if (status == "PROCESSING" && options.SkipIfOcrJobProcessing)
        {
            item.Status = "SKIPPED_PROCESSING";
            item.Reason = "Já existe OCR em processamento para a versão.";
            result.SkippedProcessing++;
            return true;
        }

        if (status == "COMPLETED" && options.SkipIfOcrAvailable)
        {
            item.Status = "SKIPPED_ALREADY_HAS_OCR";
            item.Reason = "OCR já concluído para a versão.";
            result.SkippedAlreadyHasOcr++;
            return true;
        }

        return false;
    }

    private async Task RegisterItemAsync(OcrAutoScheduleRunResultDto result, OcrAutoScheduleItemResultDto item, CancellationToken ct)
    {
        result.Items.Add(item);
        await SafeInsertRunItemAsync(result.RunId, result.TenantId, item, ct);
        var action = item.Status == "QUEUED" ? "OCR_AUTO_SCHEDULE_DOCUMENT_QUEUED" : "OCR_AUTO_SCHEDULE_DOCUMENT_SKIPPED";
        await SafeAuditAsync(result.TenantId, result.RunId, action, item.DocumentId, item.VersionId, item.Reason, result.CorrelationId, new
        {
            result.RunId,
            result.TenantId,
            item.DocumentId,
            item.VersionId,
            item.FileName,
            item.Status,
            item.Reason,
            item.OcrJobId,
            timestampUtc = DateTimeOffset.UtcNow
        }, ct);
        if (item.Status == "SKIPPED_PENDING")
            _logger.LogWarning("Documento ignorado por OCR pendente. Tenant={TenantId} DocumentId={DocumentId} VersionId={VersionId}", result.TenantId, item.DocumentId, item.VersionId);
    }

    private async Task SafeInsertRunAsync(OcrAutoScheduleRunResultDto result, CancellationToken ct)
    {
        try { result.RunId = await _repository.InsertRunAsync(result, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Não foi possível registrar início do histórico do OCR automático."); }
    }

    private async Task SafeUpdateRunAsync(OcrAutoScheduleRunResultDto result, CancellationToken ct)
    {
        try { await _repository.UpdateRunAsync(result, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Não foi possível atualizar histórico do OCR automático."); }
    }

    private async Task SafeInsertRunItemAsync(Guid runId, Guid tenantId, OcrAutoScheduleItemResultDto item, CancellationToken ct)
    {
        try { await _repository.InsertRunItemAsync(runId, tenantId, item, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Não foi possível registrar item do histórico do OCR automático."); }
    }

    private async Task SafeAuditAsync(Guid tenantId, Guid runId, string action, Guid? documentId, Guid? versionId, string? message, string correlationId, object data, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
SELECT exists (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema='ged' AND table_name='app_audit_log'
);", cancellationToken: ct));
            if (!exists)
                return;

            await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO ged.app_audit_log
(id, tenant_id, user_id, user_name, action, event_type, source, entity_name, entity_id, message, details, correlation_id, user_agent, created_at, reg_status)
VALUES
(gen_random_uuid(), @TenantId, NULL, @UserName, @Action, @EventType, @Source, @EntityName, @EntityId, @Message, @Details::jsonb, @CorrelationId, @UserAgent, now(), 'A');", new
            {
                TenantId = tenantId,
                UserName = SystemUserName,
                Action = action,
                EventType = action.EndsWith("FAILED", StringComparison.OrdinalIgnoreCase) ? "ERROR" : "INFO",
                Source = "OcrAutoScheduler",
                EntityName = "OcrAutoScheduleRun",
                EntityId = (documentId ?? runId).ToString(),
                Message = message ?? action,
                Details = JsonSerializer.Serialize(data),
                CorrelationId = correlationId,
                UserAgent = SystemUserName
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao auditar evento do OCR automático. Action={Action} RunId={RunId} DocumentId={DocumentId} VersionId={VersionId}", action, runId, documentId, versionId);
        }
    }

    private static OcrAutoScheduleOptions Normalize(OcrAutoScheduleOptions options)
    {
        options.MaxDocumentsPerRun = Math.Max(options.MaxDocumentsPerRun, 1);
        options.BatchSize = Math.Clamp(options.BatchSize, 1, options.MaxDocumentsPerRun);
        options.RunAt = string.IsNullOrWhiteSpace(options.RunAt) ? "18:00" : options.RunAt;
        options.TimeZone = string.IsNullOrWhiteSpace(options.TimeZone) ? "America/Belem" : options.TimeZone;
        options.AllowedExtensions = options.AllowedExtensions is { Length: > 0 } ? options.AllowedExtensions : [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"];
        if (options.SystemUserId == Guid.Empty)
            options.SystemUserId = null;
        return options;
    }
}
