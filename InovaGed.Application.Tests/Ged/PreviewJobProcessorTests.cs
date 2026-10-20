using System.Text.RegularExpressions;
using InovaGed.Application.Preview;
using InovaGed.Infrastructure.Preview;
using Microsoft.Extensions.Logging;

namespace InovaGed.Application.Tests.Ged;

public sealed class PreviewJobProcessorTests
{
    [Fact]
    public async Task Conversion_failure_records_one_failed_execution()
    {
        var writes = new List<PreviewStatusUpsert>();
        var result = await ExecuteAsync(writes, _ => throw new InvalidOperationException("soffice"), static (_, _, _, _) => Task.CompletedTask, failReady: false);

        Assert.Equal("ConversionFailed", result.Outcome);
        Assert.True(result.ConversionFailureRecorded);
        Assert.Equal(1, writes.Count(write => write.Status == PreviewProcessingStatus.Processing));
        Assert.Equal(1, writes.Count(write => write.Status == PreviewProcessingStatus.Error));
        Assert.All(writes, write => Assert.Equal(writes[0].AttemptId, write.AttemptId));
        var match = Regex.Match(writes.Single(write => write.Status == PreviewProcessingStatus.Error).ErrorMessage ?? "", @"suporte ([A-F0-9]{8})");
        Assert.True(match.Success);
        Assert.Equal(match.Groups[1].Value, result.SupportCode);
        Assert.Contains(result.SupportCode!, LastLog);
        Assert.Contains("Stage=CONVERSION", LastLog);
        Assert.DoesNotContain("Password", writes.Single(write => write.Status == PreviewProcessingStatus.Error).ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Conversion_timeout_stores_the_operational_message()
    {
        var writes = new List<PreviewStatusUpsert>();
        var failure = new InovaGed.Application.Common.Preview.DocumentConversionException(
            "CONVERSION_TIMEOUT",
            InovaGed.Application.Common.Preview.DocumentConversionException.TimeoutMessage,
            true,
            "timeout");
        var result = await ExecuteAsync(writes, _ => throw failure, static (_, _, _, _) => Task.CompletedTask, failReady: false);
        var message = writes.Single(write => write.Status == PreviewProcessingStatus.Error).ErrorMessage ?? "";

        Assert.Equal("ConversionFailed", result.Outcome);
        Assert.Contains(InovaGed.Application.Common.Preview.DocumentConversionException.TimeoutMessage, message, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeoutException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_status_persist_stays_recoverable_and_keeps_the_same_support_code()
    {
        var writes = new List<PreviewStatusUpsert>();
        var result = await ExecuteAsync(writes, _ => throw new InvalidOperationException("soffice"), static (_, _, _, _) => Task.CompletedTask, failReady: false, failError: true);

        Assert.Equal("FailureUnrecorded", result.Outcome);
        Assert.False(result.StatusRecorded);
        Assert.False(result.ConversionFailureRecorded);
        Assert.Matches("^[A-F0-9]{8}$", result.SupportCode);
        Assert.Contains(result.SupportCode!, LastLog);
        Assert.Contains("Stage=FAILURE_PERSIST", LastLog);
        Assert.Contains("Cause=IOException", LastLog);
        Assert.DoesNotContain(writes, write => write.Status == PreviewProcessingStatus.Error);
    }

    [Fact]
    public async Task Unrecorded_persistence_pending_does_not_escape_the_processor()
    {
        var writes = new List<PreviewStatusUpsert>();
        var first = await ExecuteAsync(writes, _ => Task.FromResult("tenant/previews/doc/ver/arquivo.pdf"), static (_, _, _, _) => Task.CompletedTask, failReady: true, failPending: true);
        var pendingLog = LastLog;
        var second = await ExecuteAsync(writes, _ => Task.FromResult("tenant/previews/doc/ver/outro.pdf"), static (_, _, _, _) => Task.CompletedTask, failReady: false);

        Assert.Equal("PersistencePending", first.Outcome);
        Assert.False(first.StatusRecorded);
        Assert.Contains("Stage=PERSISTENCE_PENDING_RECORD", pendingLog);
        Assert.Equal("Ready", second.Outcome);
        Assert.True(second.ReadyConfirmed);
    }

    [Fact]
    public void Preview_transitions_reject_skipping_and_allow_a_new_attempt()
    {
        Assert.True(PreviewStatePolicy.CanTransition(null, PreviewProcessingStatus.Pending, out _));
        Assert.False(PreviewStatePolicy.CanTransition(PreviewProcessingStatus.Pending, PreviewProcessingStatus.Ready, out var impediment));
        Assert.False(string.IsNullOrWhiteSpace(impediment));
        Assert.True(PreviewStatePolicy.CanTransition(PreviewProcessingStatus.Processing, PreviewProcessingStatus.Error, out _));
        Assert.True(PreviewStatePolicy.CanTransition(PreviewProcessingStatus.Error, PreviewProcessingStatus.Processing, out _));
        Assert.False(PreviewStatePolicy.CanTransition(PreviewProcessingStatus.Ready, PreviewProcessingStatus.Error, out _));
    }

    [Fact]
    public void Worker_cycle_backoff_grows_and_stays_bounded()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), PreviewWorker.CycleBackoff(1));
        Assert.Equal(TimeSpan.FromSeconds(10), PreviewWorker.CycleBackoff(2));
        Assert.Equal(TimeSpan.FromSeconds(120), PreviewWorker.CycleBackoff(8));
    }

    [Fact]
    public async Task Persistence_failure_keeps_generated_file_out_of_conversion_failure()
    {
        var writes = new List<PreviewStatusUpsert>();
        var result = await ExecuteAsync(writes, _ => Task.FromResult("tenant/previews/doc/ver/arquivo.pdf"), static (_, _, _, _) => Task.CompletedTask, failReady: true);

        Assert.Equal("PersistencePending", result.Outcome);
        Assert.False(result.ConversionFailureRecorded);
        Assert.Equal("tenant/previews/doc/ver/arquivo.pdf", result.PreviewPath);
        Assert.DoesNotContain(writes, write => write.Status == PreviewProcessingStatus.Error);
        Assert.Contains(writes, write => write.Status == PreviewProcessingStatus.Processing && write.PendingPreviewPath == "tenant/previews/doc/ver/arquivo.pdf" && write.PersistenceError != null);
    }

    [Fact]
    public async Task Ready_survives_notification_failure()
    {
        var writes = new List<PreviewStatusUpsert>();
        var result = await ExecuteAsync(writes, _ => Task.FromResult("tenant/previews/doc/ver/arquivo.pdf"), static (_, _, _, _) => throw new InvalidOperationException("notificação"), failReady: false);

        Assert.Equal("Ready", result.Outcome);
        Assert.True(result.ReadyConfirmed);
        Assert.True(result.NotificationFailed);
        Assert.False(result.ConversionFailureRecorded);
        Assert.Contains(writes, write => write.Status == PreviewProcessingStatus.Ready && write.ConfirmOutcome && write.PreviewPath == "tenant/previews/doc/ver/arquivo.pdf");
        Assert.DoesNotContain(writes, write => write.Status == PreviewProcessingStatus.Error);
    }

    private static string LastLog { get; set; } = string.Empty;

    private static Task<PreviewExecutionResult> ExecuteAsync(
        List<PreviewStatusUpsert> writes,
        Func<CancellationToken, Task<string>> convert,
        Func<string, string?, string?, CancellationToken, Task> notify,
        bool failReady,
        bool failError = false,
        bool failPending = false)
    {
        var logger = new CaptureLogger();
        var processor = new PreviewJobProcessor(logger);
        var attemptId = Guid.NewGuid();
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var documentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var versionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var jobId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var task = processor.ExecuteAsync(
            tenantId,
            documentId,
            versionId,
            jobId,
            DateTimeOffset.Parse("2026-10-12T13:00:00Z"),
            "ORIGINAL",
            attemptId,
            convert,
            (write, _) =>
            {
                if (failError && write.Status == PreviewProcessingStatus.Error)
                    throw new IOException("falha ao gravar FAILED");
                if (failPending && write.PersistenceError is not null)
                    throw new IOException("falha ao gravar pendência");
                writes.Add(write);
                if (failReady && write.Status == PreviewProcessingStatus.Ready)
                    throw new IOException("falha ao confirmar READY");
                return Task.CompletedTask;
            },
            notify,
            _ => Task.FromResult(false),
            CancellationToken.None);
        return Finish(task, logger);
    }

    private static async Task<PreviewExecutionResult> Finish(Task<PreviewExecutionResult> task, CaptureLogger logger)
    {
        var result = await task;
        LastLog = string.Join('\n', logger.Lines);
        return result;
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Lines { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception) + " " + exception?.GetType().Name);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
