using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace InovaGed.UiTests;

public sealed class VisualImageComparerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"inovaged-visual-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Category", "Visual")]
    public async Task CompareAsync_AcceptsSmallPixelDifferenceAndPreservesHashes()
    {
        Directory.CreateDirectory(_directory);
        var golden = Path.Combine(_directory, "golden.png");
        var actual = Path.Combine(_directory, "actual.png");
        var diff = Path.Combine(_directory, "diff.png");
        await CreateImageAsync(golden, Color.White);
        await CreateImageAsync(actual, Color.White, image => image[0, 0] = Color.Black);

        var result = await new VisualImageComparer().CompareAsync(golden, actual,
            new VisualComparisonOptions { MaximumDifferenceRatio = 0.02, PerChannelTolerance = 0, DiffPath = diff });

        Assert.True(result.Matches);
        Assert.Equal(1, result.DifferentPixels);
        Assert.Equal(0.01, result.DifferenceRatio, 5);
        Assert.NotEqual(result.GoldenSha256, result.ActualSha256);
        Assert.True(File.Exists(diff));
    }

    [Fact]
    [Trait("Category", "Visual")]
    public async Task CompareAsync_RejectsColorChangeBeyondTolerance()
    {
        Directory.CreateDirectory(_directory);
        var golden = Path.Combine(_directory, "golden.png");
        var actual = Path.Combine(_directory, "actual.png");
        await CreateImageAsync(golden, Color.White);
        await CreateImageAsync(actual, Color.Red);

        var result = await new VisualImageComparer().CompareAsync(golden, actual,
            VisualComparisonOptions.AppShell());

        Assert.False(result.Matches);
        Assert.Equal(100, result.DifferentPixels);
        Assert.Equal(1, result.DifferenceRatio);
    }

    [Fact]
    public async Task CompareAsync_ReportsEqualImagesWithoutDiff()
    {
        var (golden, actual) = await CreatePairAsync(Color.White, Color.White);
        var result = await new VisualImageComparer().CompareAsync(golden, actual);
        Assert.True(result.Matches);
        Assert.Equal(0, result.DifferentPixels);
        Assert.Null(result.DiffPath);
        Assert.Equal(result.GoldenSha256, result.ActualSha256);
    }

    [Fact]
    public async Task CompareAsync_HonorsPerChannelToleranceAndHighlightsIt()
    {
        var (golden, actual) = await CreatePairAsync(new Rgba32(100, 100, 100), new Rgba32(104, 100, 100));
        var diff = Path.Combine(_directory, "tolerated.png");
        var result = await new VisualImageComparer().CompareAsync(golden, actual,
            new VisualComparisonOptions { PerChannelTolerance = 4, DiffPath = diff, HighlightToleratedDifferences = true });
        Assert.True(result.Matches);
        Assert.Equal(0, result.DifferentPixels);
        Assert.False(File.Exists(diff));
    }

    [Fact]
    public async Task CompareAsync_ReportsBothDimensions()
    {
        Directory.CreateDirectory(_directory);
        var golden = Path.Combine(_directory, "golden.png");
        var actual = Path.Combine(_directory, "actual.png");
        await CreateImageAsync(golden, Color.White);
        using (var image = new Image<Rgba32>(4, 6, Color.White)) await image.SaveAsPngAsync(actual);
        var result = await new VisualImageComparer().CompareAsync(golden, actual);
        Assert.True(result.DimensionMismatch);
        Assert.Equal((10, 10, 4, 6), (result.GoldenWidth, result.GoldenHeight, result.ActualWidth, result.ActualHeight));
    }

    [Fact]
    public async Task CompareAsync_ObservesCancellationAndFileErrors()
    {
        var (golden, actual) = await CreatePairAsync(Color.White, Color.White);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VisualImageComparer().CompareAsync(golden, actual, cancellationToken: new CancellationToken(true)));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new VisualImageComparer().CompareAsync(Path.Combine(_directory, "missing.png"), actual));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new VisualImageComparer().CompareAsync(golden, Path.Combine(_directory, "missing.png")));
    }

    [Fact]
    public async Task CompareAsync_RejectsInvalidImage()
    {
        var (golden, _) = await CreatePairAsync(Color.White, Color.White);
        var invalid = Path.Combine(_directory, "invalid.png");
        await File.WriteAllTextAsync(invalid, "not an image");
        await Assert.ThrowsAnyAsync<Exception>(() => new VisualImageComparer().CompareAsync(golden, invalid));
    }

    private async Task<(string Golden, string Actual)> CreatePairAsync(Color goldenColor, Color actualColor)
    {
        Directory.CreateDirectory(_directory);
        var golden = Path.Combine(_directory, $"golden-{Guid.NewGuid():N}.png");
        var actual = Path.Combine(_directory, $"actual-{Guid.NewGuid():N}.png");
        await CreateImageAsync(golden, goldenColor);
        await CreateImageAsync(actual, actualColor);
        return (golden, actual);
    }

    private static async Task CreateImageAsync(string path, Color color, Action<Image<Rgba32>>? mutate = null)
    {
        using var image = new Image<Rgba32>(10, 10, color);
        mutate?.Invoke(image);
        await image.SaveAsPngAsync(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
