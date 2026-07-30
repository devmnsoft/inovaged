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
    string? DiffPath);
