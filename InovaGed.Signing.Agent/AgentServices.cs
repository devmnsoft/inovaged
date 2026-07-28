using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public interface IAgentPairingService { PairingResponse Create(PairingRequest request); bool IsValid(string token, string origin); bool Revoke(string token); }
public sealed record AgentAuthenticationResult(bool Success, Guid? PairingId, string? Origin, string? ProtocolVersion, string? Error);
public interface IAgentAuthenticationService { AgentAuthenticationResult Authenticate(HttpRequest request); }
public interface IAgentReplayProtectionService { bool Accept(string origin, string nonce); }
public interface ILocalCertificateStore { IReadOnlyList<CertificateInfo> ListUsableCertificates(); X509Certificate2? Find(string thumbprint); }
public interface ILocalConfirmationService { IResult Render(Guid operationId); }
public interface ISigningOperationStore { bool TryGet(Guid id, out SigningOperation? op); bool Cancel(Guid id); }
public interface ISigningContentDownloader { Task<DownloadedSigningContent> DownloadAsync(Uri uri, string contentToken, long expectedSize, string expectedSha256, CancellationToken ct); }
public interface ICmsDetachedLocalSigner { Task<CmsSignResult> SignAsync(SigningOperation operation, X509Certificate2 certificate, byte[] content, CancellationToken ct); }
public interface IAgentProtectedStorage { Task SaveAsync(string name, string value, CancellationToken ct); Task<string?> ReadAsync(string name, CancellationToken ct); Task DeleteAsync(string name, CancellationToken ct); }

public sealed class DownloadedSigningContent(string path, long size, string sha256) : IAsyncDisposable
{
    public string TemporaryPath { get; } = path;
    public long Size { get; } = size;
    public string Sha256 { get; } = sha256;
    public FileStream OpenRead() => new(TemporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
    public ValueTask DisposeAsync() { try { if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath); } catch { } return ValueTask.CompletedTask; }
}

public sealed class AgentReplayProtectionService : IAgentReplayProtectionService
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    public bool Accept(string origin, string nonce)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(nonce)) return false;
        var key = origin + "|" + nonce;
        lock (_seen)
        {
            foreach (var expired in _seen.Where(p => p.Value < DateTimeOffset.UtcNow.AddMinutes(-10)).Select(p => p.Key).ToArray()) _seen.Remove(expired);
            return _seen.TryAdd(key, DateTimeOffset.UtcNow);
        }
    }
}

public sealed class AgentAuthenticationService(PairingStore pairings, IAgentReplayProtectionService replay) : IAgentAuthenticationService
{
    public AgentAuthenticationResult Authenticate(HttpRequest request)
    {
        var token = request.Headers["X-InovaGed-Pairing-Token"].ToString();
        var origin = request.Headers["X-InovaGed-Origin"].ToString();
        var nonce = request.Headers["X-InovaGed-Request-Nonce"].ToString();
        var protocol = request.Headers["X-InovaGed-Agent-Protocol"].ToString();
        if (protocol != "agent-cms-detached-v1") return new(false, null, origin, protocol, "protocol_invalid");
        if (!replay.Accept(origin, nonce)) return new(false, null, origin, protocol, "replay_or_nonce_invalid");
        var pairingId = pairings.GetPairingId(token, origin);
        return pairingId is { } id ? new(true, id, origin, protocol, null) : new(false, null, origin, protocol, "pairing_invalid");
    }
}

public sealed class SigningContentDownloader(IHttpClientFactory clients, IConfiguration configuration) : ISigningContentDownloader
{
    private const int MaxRedirects = 3;

    public async Task<DownloadedSigningContent> DownloadAsync(Uri uri, string contentToken, long expectedSize, string expectedSha256, CancellationToken ct)
    {
        if (expectedSize <= 0) throw new InvalidOperationException("CONTENT_SIZE_INVALID");
        var allowedHosts = configuration.GetSection("SigningAgent:AllowedServerHosts").Get<string[]>() ?? [];
        var current = uri;
        using var client = clients.CreateClient("signing-content");
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            await ValidateUriAsync(current, allowedHosts, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Add("X-InovaGed-Content-Token", contentToken);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                if (redirect == MaxRedirects) throw new InvalidOperationException("TOO_MANY_REDIRECTS");
                current = new Uri(current, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > 0 && contentLength.Value != expectedSize) throw new InvalidOperationException("CONTENT_SIZE_MISMATCH");
            var temp = Path.Combine(Path.GetTempPath(), "inovaged-signing-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var sha = SHA256.Create();
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > expectedSize) throw new InvalidOperationException("CONTENT_TOO_LARGE");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                Array.Clear(buffer);
                var actualHash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                if (total != expectedSize) throw new InvalidOperationException("CONTENT_SIZE_MISMATCH");
                if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, expectedSha256)) throw new InvalidOperationException("DOCUMENT_CHANGED");
                return new DownloadedSigningContent(temp, total, actualHash);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }
        throw new InvalidOperationException("TOO_MANY_REDIRECTS");
    }

    private static bool IsRedirect(HttpStatusCode status) => (int)status is >= 300 and <= 399;
    private static async Task ValidateUriAsync(Uri uri, string[] allowedHosts, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("HTTPS_REQUIRED");
        if (allowedHosts.Length > 0 && !allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("HOST_NOT_ALLOWED");
        var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        if (addresses.Any(IsBlocked)) throw new InvalidOperationException("PRIVATE_OR_METADATA_IP_BLOCKED");
    }

    private static bool IsBlocked(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.Equals(IPAddress.Parse("169.254.169.254"))) return true;
        var b = ip.GetAddressBytes();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254))
            || ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal);
    }
}
