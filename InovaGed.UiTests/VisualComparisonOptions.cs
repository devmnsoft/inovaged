namespace InovaGed.UiTests;

public sealed record VisualComparisonOptions
{
    public double MaximumDifferenceRatio { get; init; } = 0.015;

    public byte PerChannelTolerance { get; init; } = 8;

    public string? DiffPath { get; init; }

    public bool HighlightToleratedDifferences { get; init; }

    public static VisualComparisonOptions AppShell(string? diffPath = null) =>
        new() { MaximumDifferenceRatio = 0.005, DiffPath = diffPath };

    public static VisualComparisonOptions StableComponent(string? diffPath = null) =>
        new() { MaximumDifferenceRatio = 0.0075, DiffPath = diffPath };

    public static VisualComparisonOptions Page(string? diffPath = null) =>
        new() { MaximumDifferenceRatio = 0.015, DiffPath = diffPath };

    public static VisualComparisonOptions LongPage(string? diffPath = null) =>
        new() { MaximumDifferenceRatio = 0.02, DiffPath = diffPath };
}
