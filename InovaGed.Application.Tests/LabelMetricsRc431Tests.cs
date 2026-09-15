using InovaGed.Infrastructure.PhysicalArchive;

namespace InovaGed.Application.Tests;

public sealed class LabelMetricsRc431Tests
{
    private static readonly TimeZoneInfo Belem = ResolveBelem();

    [Fact]
    public void label_metrics_uses_timestamp_boundaries()
    {
        Assert.Contains("printed_at>=@TodayStartUtc", LabelPrintJobService.MetricsSql);
        Assert.Contains("requested_at<@ToExclusiveUtc", LabelPrintJobService.MetricsSql);
        Assert.DoesNotContain("current_date", LabelPrintJobService.MetricsSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void label_metrics_today_respects_local_timezone()
    {
        var result = LabelPrintJobService.BuildMetricsBoundaries(
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), Belem, null, null);

        Assert.Equal(new DateTimeOffset(2026, 9, 15, 3, 0, 0, TimeSpan.Zero), result.TodayStartUtc);
    }

    [Fact]
    public void label_metrics_today_uses_half_open_range()
    {
        Assert.Contains("printed_at>=@TodayStartUtc and printed_at<@TomorrowStartUtc", LabelPrintJobService.MetricsSql);
    }

    [Fact]
    public void label_metrics_stale_queue_uses_timestamp_not_interval()
    {
        Assert.DoesNotContain("interval", LabelPrintJobService.MetricsSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TimeSpan", LabelPrintJobService.MetricsSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void label_metrics_is_tenant_scoped()
    {
        Assert.Contains("tenant_id=@TenantId", LabelPrintJobService.MetricsSql);
    }

    [Fact]
    public void label_metrics_excludes_inactive_records()
    {
        Assert.Contains("reg_status='A'", LabelPrintJobService.MetricsSql);
    }

    [Fact]
    public void label_metrics_printed_today_counts_only_printed()
    {
        Assert.Contains("status='PRINTED' and printed_at", LabelPrintJobService.MetricsSql);
        Assert.DoesNotContain("PDF_GENERATED' and printed_at", LabelPrintJobService.MetricsSql);
    }

    [Fact]
    public void label_metrics_filters_are_converted_from_local_dates()
    {
        var result = LabelPrintJobService.BuildMetricsBoundaries(
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), Belem,
            new DateTime(2026, 9, 10), new DateTime(2026, 9, 12));

        Assert.Equal(new DateTimeOffset(2026, 9, 10, 3, 0, 0, TimeSpan.Zero), result.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 3, 0, 0, TimeSpan.Zero), result.ToExclusiveUtc);
    }

    [Fact]
    public void label_metrics_failure_does_not_expose_sql()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "InovaGed.Web", "Controller", "LabelsController.cs"));
        var methodStart = source.IndexOf("private async Task<LabelPrintQueueMetrics> LoadMetricsAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("[HttpGet(\"/Labels/Guide\")]", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];
        Assert.Contains("Indicadores temporariamente indisponíveis", File.ReadAllText(Path.Combine(Root(), "InovaGed.Web", "Views", "Labels", "Index.cshtml")));
        Assert.Contains("Operation={Operation}", method);
        Assert.DoesNotContain("ex.Message", method);
    }

    private static TimeZoneInfo ResolveBelem()
    {
        foreach (var id in new[] { "America/Belem", "E. South America Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new InvalidOperationException("Timezone America/Belem não disponível para o teste.");
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
