using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
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
        {
            return new(false, actual.Width, actual.Height, (long)actual.Width * actual.Height,
                (long)Math.Max(golden.Width, actual.Width) * Math.Max(golden.Height, actual.Height),
                1, goldenHash, actualHash, null);
        }

        var total = (long)golden.Width * golden.Height;
        long different = 0;
        Image<Rgba32>? diff = options.DiffPath is null ? null : new Image<Rgba32>(golden.Width, golden.Height);
        try
        {
            for (var y = 0; y < golden.Height; y++)
            {
                var goldenRow = golden.DangerousGetPixelRowMemory(y).Span;
                var actualRow = actual.DangerousGetPixelRowMemory(y).Span;
                var diffMemory = diff is null
                    ? Memory<Rgba32>.Empty
                    : diff.DangerousGetPixelRowMemory(y);
                var diffRow = diffMemory.Span;
                for (var x = 0; x < golden.Width; x++)
                {
                    var changed = IsDifferent(goldenRow[x], actualRow[x], options.PerChannelTolerance);
                    if (changed) different++;
                    if (diff is not null)
                        diffRow[x] = changed ? new Rgba32(239, 68, 68, 255) : Dim(actualRow[x]);
                }
            }

            var ratio = total == 0 ? 0 : different / (double)total;
            string? diffPath = null;
            if (different > 0 && diff is not null)
            {
                diffPath = options.DiffPath;
                var directory = Path.GetDirectoryName(diffPath!);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                await diff.SaveAsPngAsync(diffPath!, cancellationToken);
            }

            return new(ratio <= options.MaximumDifferenceRatio, golden.Width, golden.Height,
                total, different, ratio, goldenHash, actualHash, diffPath);
        }
        finally
        {
            diff?.Dispose();
        }
    }

    private static bool IsDifferent(Rgba32 left, Rgba32 right, byte tolerance) =>
        Math.Abs(left.R - right.R) > tolerance || Math.Abs(left.G - right.G) > tolerance ||
        Math.Abs(left.B - right.B) > tolerance || Math.Abs(left.A - right.A) > tolerance;

    private static Rgba32 Dim(Rgba32 pixel) => new(
        (byte)(pixel.R * 0.25), (byte)(pixel.G * 0.25), (byte)(pixel.B * 0.25), 255);
}
