using System.Net;
using System.Security.Cryptography.X509Certificates;

public interface IAgentPairingService { PairingResponse Create(PairingRequest request); bool IsValid(string token, string origin); bool Revoke(string token); }
public interface IAgentAuthenticationService { bool Authenticate(HttpRequest request); }
public interface IAgentReplayProtectionService { bool Accept(string origin, string nonce); }
public interface ILocalCertificateStore { IReadOnlyList<CertificateInfo> ListUsableCertificates(); X509Certificate2? Find(string thumbprint); }
public interface ILocalConfirmationService { IResult Render(Guid operationId); }
public interface ISigningOperationStore { bool TryGet(Guid id, out SigningOperation? op); bool Cancel(Guid id); }
public interface ISigningContentDownloader { Task<byte[]> DownloadAsync(Uri uri, string contentToken, long maxBytes, CancellationToken ct); }
public interface ICmsDetachedLocalSigner { Task<CmsSignResult> SignAsync(SigningOperation operation, X509Certificate2 certificate, byte[] content, CancellationToken ct); }
public interface IAgentProtectedStorage { Task SaveAsync(string name, string value, CancellationToken ct); Task<string?> ReadAsync(string name, CancellationToken ct); Task DeleteAsync(string name, CancellationToken ct); }

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
    public bool Authenticate(HttpRequest request)
    {
        var token = request.Headers["X-InovaGed-Pairing-Token"].ToString();
        var origin = request.Headers["X-InovaGed-Origin"].ToString();
        var nonce = request.Headers["X-InovaGed-Request-Nonce"].ToString();
        var protocol = request.Headers["X-InovaGed-Agent-Protocol"].ToString();
        return protocol == "agent-cms-detached-v1" && replay.Accept(origin, nonce) && pairings.IsValid(token, origin);
    }
}

public sealed class SigningContentDownloader(IHttpClientFactory clients, IConfiguration configuration) : ISigningContentDownloader
{
    public async Task<byte[]> DownloadAsync(Uri uri, string contentToken, long maxBytes, CancellationToken ct)
    {
        await ValidateUriAsync(uri, configuration.GetSection("SigningAgent:AllowedServerHosts").Get<string[]>() ?? [], ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-InovaGed-Content-Token", contentToken);
        using var response = await clients.CreateClient("signing-content").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.Headers.Location is { } redirect) await ValidateUriAsync(new Uri(uri, redirect), configuration.GetSection("SigningAgent:AllowedServerHosts").Get<string[]>() ?? [], ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidOperationException("CONTENT_TOO_LARGE");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920]; int read; long total = 0;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0) { total += read; if (total > maxBytes) throw new InvalidOperationException("CONTENT_TOO_LARGE"); ms.Write(buffer, 0, read); }
        return ms.ToArray();
    }
    private static async Task ValidateUriAsync(Uri uri, string[] allowedHosts, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("HTTPS_REQUIRED");
        if (allowedHosts.Length > 0 && !allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("HOST_NOT_ALLOWED");
        var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        if (addresses.Any(IsBlocked)) throw new InvalidOperationException("PRIVATE_OR_METADATA_IP_BLOCKED");
    }
    private static bool IsBlocked(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true; var b = ip.GetAddressBytes();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
    }
}
