namespace InovaGed.Application.Signatures;

public sealed record SignatureResult(bool Success, string Provider, string SignatureHash, string? Error);

public interface ISignatureProvider
{
    string Name { get; }
    Task<SignatureResult> SignHtmlAsync(string html, string signerName, string? signerDocument, CancellationToken ct);
}