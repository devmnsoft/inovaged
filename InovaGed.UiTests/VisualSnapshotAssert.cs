using Xunit.Sdk;

namespace InovaGed.UiTests;

public static class VisualSnapshotAssert
{
    public static async Task MatchAsync(
        byte[] actualBytes,
        string goldenPath,
        string actualPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actualBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(goldenPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualPath);

        if (!File.Exists(actualPath))
        {
            throw new XunitException($"O screenshot atual não existe: {actualPath}");
        }

        if (actualBytes.Length == 0)
        {
            throw new XunitException($"O screenshot atual está vazio: {actualPath}");
        }

        var updateBaselines = string.Equals(
            Environment.GetEnvironmentVariable("INOVAGED_UPDATE_UI_BASELINES"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var runningInCi = string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (updateBaselines && runningInCi)
        {
            throw new InvalidOperationException("A atualização automática de baselines é proibida no CI.");
        }

        if (updateBaselines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            await File.WriteAllBytesAsync(goldenPath, actualBytes, cancellationToken);
            Console.WriteLine($"Baseline atualizado localmente para revisão visual explícita: {goldenPath}");
            return;
        }

        if (!File.Exists(goldenPath))
        {
            throw new XunitException(
                $"Visual snapshot golden ausente.\n\nGolden:\n{goldenPath}\n\nActual:\n{actualPath}\n\n" +
                "Crie o baseline localmente e aprove-o somente após revisão visual explícita.");
        }

        var diffPath = Path.Combine(Path.GetDirectoryName(actualPath)!, "diff", Path.GetFileName(actualPath));
        var result = await new VisualImageComparer().CompareAsync(
            goldenPath,
            actualPath,
            VisualComparisonOptions.Page(diffPath),
            cancellationToken);
        if (!result.Matches)
        {
            throw new XunitException(
                $"Visual snapshot mismatch.\n\nGolden:\n{goldenPath}\n\nActual:\n{actualPath}\n\n" +
                $"Pixels diferentes: {result.DifferentPixels}/{result.TotalPixels} ({result.DifferenceRatio:P3})\n\n" +
                $"Diff:\n{result.DiffPath ?? "não gerado (dimensões divergentes)"}\n\n" +
                $"Golden SHA-256:\n{result.GoldenSha256}\n\nActual SHA-256:\n{result.ActualSha256}\n\n" +
                "Atualize o baseline somente após revisão visual explícita.");
        }
    }
}
