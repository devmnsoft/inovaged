using InovaGed.Application.Observability;
using InovaGed.Infrastructure.Observability;
namespace InovaGed.Application.Tests;

public sealed class ErrorBudgetCalculatorTests
{
    [Fact, Trait("Category", "Architecture")]
    public void Calculates_healthy_and_exhausted_budgets()
    {
        var calculator = new ErrorBudgetCalculator();
        Assert.Equal(ErrorBudgetStatus.Healthy, calculator.Calculate(99m, new(1000, 998)).Status);
        Assert.Equal(ErrorBudgetStatus.Exhausted, calculator.Calculate(99m, new(100, 98)).Status);
        Assert.Equal(ErrorBudgetStatus.NotEnoughData, calculator.Calculate(99m, new(0, 0)).Status);
    }
}
public sealed class TelemetrySanitizerTests
{
    [Theory, Trait("Category", "Architecture")]
    [InlineData("password", "unsafe")]
    [InlineData("Authorization", "Bearer unsafe")]
    [InlineData("cpf", "123.456.789-00")]
    public void Blocks_sensitive_keys(string key, string value) => Assert.Equal("[REDACTED]", new TelemetrySanitizer().Sanitize(key, value));
    [Fact, Trait("Category", "Architecture")]
    public void Masks_sensitive_values()
    {
        var output = new TelemetrySanitizer().Sanitize("note", "123.456.789-00 user@example.test 10.0.0.1 /var/private/file");
        Assert.DoesNotContain("123.456", output); Assert.DoesNotContain("example.test", output); Assert.DoesNotContain("10.0.0.1", output); Assert.DoesNotContain("/var/private", output);
    }
}
public sealed class MetricCardinalityContractTests
{
    [Fact, Trait("Category", "Architecture")]
    public void Request_metrics_do_not_contain_forbidden_dimensions()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "InovaGed.Web", "Observability", "CorrelationIdMiddleware.cs")).ToLowerInvariant();
        foreach (var label in new[] { "document_id", "user_id", "patient_id", "trace_id\"", "span_id\"", "correlation_id\"", "raw_url", "query_string", "file_name", "exception_message" }) Assert.DoesNotContain("{ \"" + label, source);
    }
    private static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "InovaGed.sln"))) d=d.Parent; return d!.FullName; }
}
public sealed class IncidentLifecycleTests
{
    [Fact, Trait("Category", "Architecture")]
    public void Rejects_invalid_transition() { Assert.True(IncidentLifecycle.CanTransition(IncidentStatus.Detected, IncidentStatus.Open)); Assert.False(IncidentLifecycle.CanTransition(IncidentStatus.Open, IncidentStatus.Closed)); }
}
public sealed class AlertDeduplicationTests
{
    [Fact, Trait("Category", "Architecture")]
    public void Key_excludes_request_identifiers()
    {
        var key=AlertDeduplication.Key(new("HTTP_5XX_HIGH", "web", "cluster-a", "prod", 1, DateTimeOffset.UtcNow));
        Assert.Equal("http_5xx_high:web:cluster-a:prod", key);
    }
}
