using InovaGed.Application.Common.Preview;
using InovaGed.Application.Preview;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Preview;

public sealed class PreviewExecutionResult
{
    public string Outcome { get; init; } = string.Empty;
    public bool ConversionFailureRecorded { get; init; }
    public bool ReadyConfirmed { get; init; }
    public bool NotificationFailed { get; init; }
    public bool StatusRecorded { get; init; } = true;
    public string? SupportCode { get; init; }
    public string? PreviewPath { get; init; }
}

public sealed class PreviewJobProcessor
{
    private readonly ILogger _logger;

    public PreviewJobProcessor(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<PreviewExecutionResult> ExecuteAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid? jobId,
        DateTimeOffset? requestedAt,
        string? requestedAtKind,
        Guid attemptId,
        Func<CancellationToken, Task<string>> convert,
        Func<PreviewStatusUpsert, CancellationToken, Task> persist,
        Func<string, string?, string?, CancellationToken, Task> notify,
        Func<CancellationToken, Task<bool>> isCanceled,
        CancellationToken ct)
    {
        if (await isCanceled(ct))
        {
            await persist(Write(tenantId, versionId, PreviewProcessingStatus.Canceled, requestedAt, requestedAtKind, attemptId, finishedAt: DateTimeOffset.UtcNow), ct);
            return new PreviewExecutionResult { Outcome = "Canceled" };
        }

        await persist(Write(tenantId, versionId, PreviewProcessingStatus.Processing, requestedAt, requestedAtKind, attemptId), ct);
        await NotifyAsync(notify, "PROCESSING", null, "Gerando preview", ct);

        string path;
        try
        {
            path = await convert(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var supportCode = PreviewSupportCode.Create();
            var publicMessage = ex is DocumentConversionException conversion
                ? $"{conversion.UserMessage} Informe o suporte {supportCode}."
                : PreviewSupportCode.PublicMessage(supportCode);
            _logger.LogError(ex, "Falha de conversão do preview. SupportCode={SupportCode} Tenant={TenantId} Document={DocumentId} Version={VersionId} Job={JobId} Attempt={AttemptId} Stage={Stage} Cause={Cause}", supportCode, tenantId, documentId, versionId, jobId, attemptId, "CONVERSION", ex.GetType().Name);
            try
            {
                await persist(Write(tenantId, versionId, PreviewProcessingStatus.Error, requestedAt, requestedAtKind, attemptId, errorMessage: publicMessage, finishedAt: DateTimeOffset.UtcNow), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx, "Falha ao persistir FAILED do preview. O job permanece recuperável e os demais continuam. SupportCode={SupportCode} Tenant={TenantId} Document={DocumentId} Version={VersionId} Job={JobId} Attempt={AttemptId} Stage={Stage} Cause={Cause}", supportCode, tenantId, documentId, versionId, jobId, attemptId, "FAILURE_PERSIST", persistEx.GetType().Name);
                return new PreviewExecutionResult { Outcome = "FailureUnrecorded", SupportCode = supportCode, StatusRecorded = false };
            }

            await NotifyAsync(notify, "ERROR", null, publicMessage, ct);
            return new PreviewExecutionResult { Outcome = "ConversionFailed", ConversionFailureRecorded = true, SupportCode = supportCode };
        }

        try
        {
            await persist(Write(tenantId, versionId, PreviewProcessingStatus.Ready, requestedAt, requestedAtKind, attemptId, previewPath: path, finishedAt: DateTimeOffset.UtcNow, confirmOutcome: true), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var supportCode = PreviewSupportCode.Create();
            _logger.LogError(ex, "PDF de preview gerado, mas a confirmação READY falhou. A conversão não será repetida enquanto o arquivo existir. SupportCode={SupportCode} Tenant={TenantId} Document={DocumentId} Version={VersionId} Job={JobId} Attempt={AttemptId} Stage={Stage} Cause={Cause}", supportCode, tenantId, documentId, versionId, jobId, attemptId, "READY_PERSIST", ex.GetType().Name);
            try
            {
                await persist(Write(
                    tenantId,
                    versionId,
                    PreviewProcessingStatus.Processing,
                    requestedAt,
                    requestedAtKind,
                    attemptId,
                    persistenceError: "Arquivo de preview gerado e aguardando confirmação do estado. A falha foi de persistência, não de conversão.",
                    pendingPreviewPath: path), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception persistenceEx)
            {
                _logger.LogError(persistenceEx, "Falha ao registrar pendência de persistência do preview. O arquivo gerado permanece e o job segue recuperável. SupportCode={SupportCode} Tenant={TenantId} Document={DocumentId} Version={VersionId} Job={JobId} Attempt={AttemptId} Stage={Stage} Cause={Cause}", supportCode, tenantId, documentId, versionId, jobId, attemptId, "PERSISTENCE_PENDING_RECORD", persistenceEx.GetType().Name);
                return new PreviewExecutionResult { Outcome = "PersistencePending", PreviewPath = path, StatusRecorded = false, SupportCode = supportCode };
            }

            return new PreviewExecutionResult { Outcome = "PersistencePending", PreviewPath = path, SupportCode = supportCode };
        }

        var notificationFailed = false;
        try
        {
            await notify("READY", $"/Ged/PreviewVersion?versionId={versionId}", null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            notificationFailed = true;
            _logger.LogWarning(ex, "Preview READY confirmado. A falha ficou restrita à notificação. Tenant={TenantId} Version={VersionId}", tenantId, versionId);
        }

        return new PreviewExecutionResult { Outcome = "Ready", ReadyConfirmed = true, NotificationFailed = notificationFailed, PreviewPath = path };
    }

    private async Task NotifyAsync(Func<string, string?, string?, CancellationToken, Task> notify, string status, string? url, string? message, CancellationToken ct)
    {
        try
        {
            await notify(status, url, message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao notificar status {Status} do preview.", status);
        }
    }

    private static PreviewStatusUpsert Write(
        Guid tenantId,
        Guid versionId,
        PreviewProcessingStatus status,
        DateTimeOffset? requestedAt,
        string? requestedAtKind,
        Guid attemptId,
        string? previewPath = null,
        string? errorMessage = null,
        string? persistenceError = null,
        string? pendingPreviewPath = null,
        DateTimeOffset? finishedAt = null,
        bool confirmOutcome = false) => new()
    {
        TenantId = tenantId,
        VersionId = versionId,
        Status = status,
        PreviewPath = previewPath,
        ErrorMessage = errorMessage,
        RequestedAt = requestedAt,
        RequestedAtKind = requestedAtKind,
        FinishedAt = finishedAt,
        AttemptId = attemptId,
        PersistenceError = persistenceError,
        PendingPreviewPath = pendingPreviewPath,
        ConfirmOutcome = confirmOutcome
    };
}
