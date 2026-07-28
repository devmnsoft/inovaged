using InovaGed.Application.Signatures;
using InovaGed.Infrastructure.Signatures;

namespace InovaGed.Application.Tests;

public sealed class NotConfiguredSignatureValidationServiceTests
{
    private readonly NotConfiguredSignatureValidationService _service = new();

    [Fact]
    [Trait("Category", "CmsContract")]
    public async Task ValidateAsync_returns_an_honest_not_verifiable_report()
    {
        var command = new ValidateSignatureCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [], "test-correlation");

        var report = await _service.ValidateAsync(command, CancellationToken.None);

        Assert.Equal(SignatureValidationStatus.NOT_VERIFIABLE, report.Status);
        Assert.Equal(SignatureProfile.UNKNOWN, report.Profile);
        Assert.NotEmpty(report.Checks);
        Assert.DoesNotContain(report.Checks, check => check.Status == SignatureValidationStatus.VALID);
        Assert.False(string.IsNullOrWhiteSpace(report.EngineVersion));
    }

    [Fact]
    [Trait("Category", "CmsContract")]
    public async Task ValidateDetachedAsync_does_not_consume_or_interpret_inputs()
    {
        await using var content = new MemoryStream([10, 20, 30, 40]);
        content.Position = 2;
        var initialPosition = content.Position;

        var report = await _service.ValidateDetachedAsync(
            content,
            new byte[] { 0xff, 0x00, 0xff },
            new byte[] { 0x00, 0xff, 0x00 },
            CancellationToken.None);

        Assert.Equal(SignatureValidationStatus.NOT_VERIFIABLE, report.Status);
        Assert.DoesNotContain(report.Checks, check => check.Status == SignatureValidationStatus.VALID);
        Assert.Equal(initialPosition, content.Position);
        Assert.Contains(report.Checks, check => check.Name == "CMS_VALIDATION_NOT_CONFIGURED");
    }

    [Fact]
    [Trait("Category", "CmsContract")]
    public async Task Both_entry_points_honor_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var command = new ValidateSignatureCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [], "test-correlation");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.ValidateAsync(command, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.ValidateDetachedAsync(
                Stream.Null, ReadOnlyMemory<byte>.Empty, null, cancellation.Token));
    }
}
