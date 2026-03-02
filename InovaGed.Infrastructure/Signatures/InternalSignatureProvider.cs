using System.Security.Cryptography;
using System.Text;

namespace InovaGed.Infrastructure.Signatures;

public sealed class InternalSignatureProvider : InovaGed.Application.Signatures.ISignatureProvider
{
    public string Name => "INTERNAL";

    public Task<InovaGed.Application.Signatures.SignatureResult> SignHtmlAsync(
        string html, string signerName, string? signerDocument, CancellationToken ct)
    {
        var payload = $"{signerName}|{signerDocument}|{html}";
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return Task.FromResult(new InovaGed.Application.Signatures.SignatureResult(true, Name, hash, null));
    }
}