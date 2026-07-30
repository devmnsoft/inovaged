using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace InovaGed.UiTests;

public sealed class VisualImageComparer
{
    public async Task<VisualComparisonResult> CompareAsync(
        string goldenPath,
        string actualPath,
        VisualComparisonOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goldenPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualPath);
        options ??= new VisualComparisonOptions();

        if (options.MaximumDifferenceRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "A proporção máxima deve estar entre zero e um.");

        var goldenBytes = await File.ReadAllBytesAsync(goldenPath, cancellationToken);
        var actualBytes = await File.ReadAllBytesAsync(actualPath, cancellationToken);
        var goldenHash = Convert.ToHexString(SHA256.HashData(goldenBytes));
        var actualHash = Convert.ToHexString(SHA256.HashData(actualBytes));

        using var golden = Image.Load<Rgba32>(goldenBytes);
        using var actual = Image.Load<Rgba32>(actualBytes);
        if (golden.Width != actual.Width || golden.Height != actual.Height)
            return CreateDimensionMismatchResult(golden, actual, goldenHash, actualHash);

        var totalPixels = (long)golden.Width * golden.Height;
        var pixelResult = ComparePixels(golden, actual, options);
        try
        {
            var differenceRatio = totalPixels == 0 ? 0 : pixelResult.DifferentPixels / (double)totalPixels;
            string? generatedDiffPath = null;
            if (pixelResult.DifferentPixels > 0 && pixelResult.DiffImage is not null &&
                !string.IsNullOrWhiteSpace(options.DiffPath))
            {
                generatedDiffPath = options.DiffPath;
                var directory = Path.GetDirectoryName(generatedDiffPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                await pixelResult.DiffImage.SaveAsPngAsync(generatedDiffPath, cancellationToken);
            }

            return new VisualComparisonResult(differenceRatio <= options.MaximumDifferenceRatio,
                golden.Width, golden.Height, totalPixels, pixelResult.DifferentPixels, differenceRatio,
                goldenHash, actualHash, generatedDiffPath)
            {
                GoldenWidth = golden.Width,
                GoldenHeight = golden.Height,
                ActualWidth = actual.Width,
                ActualHeight = actual.Height
            };
        }
        finally
        {
            pixelResult.DiffImage?.Dispose();
        }
    }

    private static PixelComparisonResult ComparePixels(
        Image<Rgba32> golden,
        Image<Rgba32> actual,
        VisualComparisonOptions options)
    {
        var differentPixels = 0L;
        Image<Rgba32>? diffImage = string.IsNullOrWhiteSpace(options.DiffPath)
            ? null
            : new Image<Rgba32>(golden.Width, golden.Height);

        for (var y = 0; y < golden.Height; y++)
        {
            var goldenRow = golden.DangerousGetPixelRowMemory(y).Span;
            var actualRow = actual.DangerousGetPixelRowMemory(y).Span;
            var diffRow = diffImage is null
                ? Span<Rgba32>.Empty
                : diffImage.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < golden.Width; x++)
            {
                var exactDifference = !goldenRow[x].Equals(actualRow[x]);
                var changed = IsDifferent(goldenRow[x], actualRow[x], options.PerChannelTolerance);
                if (changed) differentPixels++;
                if (diffImage is not null)
                    diffRow[x] = changed
                        ? new Rgba32(239, 68, 68, 255)
                        : exactDifference && options.HighlightToleratedDifferences
                            ? new Rgba32(234, 179, 8, 255)
                            : Dim(actualRow[x]);
            }
        }

        return new PixelComparisonResult(differentPixels, diffImage);
    }

    private static VisualComparisonResult CreateDimensionMismatchResult(
        Image<Rgba32> golden, Image<Rgba32> actual, string goldenHash, string actualHash)
    {
        var actualPixels = (long)actual.Width * actual.Height;
        var comparedArea = (long)Math.Max(golden.Width, actual.Width) * Math.Max(golden.Height, actual.Height);
        return new VisualComparisonResult(false, actual.Width, actual.Height, actualPixels, comparedArea,
            1, goldenHash, actualHash, null)
        {
            GoldenWidth = golden.Width,
            GoldenHeight = golden.Height,
            ActualWidth = actual.Width,
            ActualHeight = actual.Height,
            DimensionMismatch = true
        };
    }

    private static bool IsDifferent(Rgba32 left, Rgba32 right, byte tolerance) =>
        Math.Abs(left.R - right.R) > tolerance || Math.Abs(left.G - right.G) > tolerance ||
        Math.Abs(left.B - right.B) > tolerance || Math.Abs(left.A - right.A) > tolerance;

    private static Rgba32 Dim(Rgba32 pixel) => new(
        (byte)(pixel.R * 0.25), (byte)(pixel.G * 0.25), (byte)(pixel.B * 0.25), 255);

    private sealed record PixelComparisonResult(long DifferentPixels, Image<Rgba32>? DiffImage);
}
