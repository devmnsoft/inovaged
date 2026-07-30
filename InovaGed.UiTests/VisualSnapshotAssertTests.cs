using Xunit.Sdk;

namespace InovaGed.UiTests;

public sealed class VisualSnapshotAssertTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"inovaged-visual-{Guid.NewGuid():N}");
    private readonly string? _update = Environment.GetEnvironmentVariable("INOVAGED_UPDATE_UI_BASELINES");
    private readonly string? _ci = Environment.GetEnvironmentVariable("CI");

    [Fact]
    public async Task Matching_files_pass()
    {
        var (golden, actual) = await CreateFilesAsync([1, 2, 3], [1, 2, 3]);
        await VisualSnapshotAssert.MatchAsync([1, 2, 3], golden, actual);
    }

    [Fact]
    public async Task Missing_golden_fails_clearly()
    {
        var actual = Path.Combine(_root, "actual.png");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(actual, [1]);
        var error = await Assert.ThrowsAsync<XunitException>(() =>
            VisualSnapshotAssert.MatchAsync([1], Path.Combine(_root, "missing.png"), actual));
        Assert.Contains("golden ausente", error.Message);
    }

    [Fact]
    public async Task Empty_actual_fails_clearly()
    {
        var (golden, actual) = await CreateFilesAsync([1], []);
        var error = await Assert.ThrowsAsync<XunitException>(() => VisualSnapshotAssert.MatchAsync([], golden, actual));
        Assert.Contains("está vazio", error.Message);
    }

    [Fact]
    public async Task Different_hash_preserves_actual_and_reports_hashes()
    {
        var (golden, actual) = await CreateFilesAsync([1], [2]);
        var error = await Assert.ThrowsAsync<XunitException>(() => VisualSnapshotAssert.MatchAsync([2], golden, actual));
        Assert.Contains("Visual snapshot mismatch", error.Message);
        Assert.Contains("SHA-256", error.Message);
        Assert.True(File.Exists(actual));
    }

    [Fact]
    public async Task Missing_actual_directory_fails_clearly()
    {
        var error = await Assert.ThrowsAsync<XunitException>(() => VisualSnapshotAssert.MatchAsync(
            [1], Path.Combine(_root, "golden.png"), Path.Combine(_root, "missing", "actual.png")));
        Assert.Contains("não existe", error.Message);
    }

    [Fact]
    public async Task Local_update_creates_golden()
    {
        Environment.SetEnvironmentVariable("INOVAGED_UPDATE_UI_BASELINES", "true");
        Environment.SetEnvironmentVariable("CI", null);
        var actual = Path.Combine(_root, "actual.png");
        var golden = Path.Combine(_root, "nested", "golden.png");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(actual, [4, 5]);
        await VisualSnapshotAssert.MatchAsync([4, 5], golden, actual);
        Assert.Equal(new byte[] { 4, 5 }, await File.ReadAllBytesAsync(golden));
    }

    [Fact]
    public async Task Update_is_blocked_in_ci()
    {
        Environment.SetEnvironmentVariable("INOVAGED_UPDATE_UI_BASELINES", "true");
        Environment.SetEnvironmentVariable("CI", "true");
        var actual = Path.Combine(_root, "actual.png");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(actual, [1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => VisualSnapshotAssert.MatchAsync(
            [1], Path.Combine(_root, "golden.png"), actual));
    }

    private async Task<(string Golden, string Actual)> CreateFilesAsync(byte[] goldenBytes, byte[] actualBytes)
    {
        Directory.CreateDirectory(_root);
        var golden = Path.Combine(_root, "golden.png");
        var actual = Path.Combine(_root, "actual.png");
        await File.WriteAllBytesAsync(golden, goldenBytes);
        await File.WriteAllBytesAsync(actual, actualBytes);
        return (golden, actual);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("INOVAGED_UPDATE_UI_BASELINES", _update);
        Environment.SetEnvironmentVariable("CI", _ci);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
