namespace InovaGed.UiTests;

public sealed record VisualComparisonResult(
    bool Matches,
    int Width,
    int Height,
    long TotalPixels,
    long DifferentPixels,
    double DifferenceRatio,
    string GoldenSha256,
    string ActualSha256,
    string? DiffPath)
{
    public int GoldenWidth { get; init; } = Width;
    public int GoldenHeight { get; init; } = Height;
    public int ActualWidth { get; init; } = Width;
    public int ActualHeight { get; init; } = Height;
    public bool DimensionMismatch { get; init; }
}
