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
