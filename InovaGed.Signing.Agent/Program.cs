using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;


var cliArgs = args.Where(a => !string.Equals(a, "--", StringComparison.Ordinal)).ToArray();
if (cliArgs.Length > 0)
{
    var command = cliArgs[0].TrimStart('-').ToLowerInvariant();
    if (command is "version")
    {
        Console.WriteLine("InovaGed Signing Agent agent-cms-detached-v1");
        return;
    }
    if (command is "doctor")
    {
        var configuredUrls = Environment.GetEnvironmentVariable("SigningAgent__Urls") ?? "https://127.0.0.1:17891;https://[::1]:17891";
        var allowedHosts = (Environment.GetEnvironmentVariable("SigningAgent__AllowedServerHosts") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var candidate in configuredUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var uri = new Uri(candidate);
            if (uri.Scheme != Uri.UriSchemeHttps || !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address))
            {
                Console.Error.WriteLine($"Invalid listener URL for local agent: {candidate}");
                Environment.ExitCode = 2;
                return;
            }
        }
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InovaGed", "SigningAgent");
        Directory.CreateDirectory(dataDir);
        using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadOnly);
            _ = store.Certificates.Count;
        }
        if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase) && allowedHosts.Length == 0)
        {
            Console.Error.WriteLine("SigningAgent__AllowedServerHosts is required in Production.");
            Environment.ExitCode = 3;
            return;
        }
        Console.WriteLine("Signing Agent doctor: healthy");
        return;
    }
    if (command is "install" or "uninstall" or "rotate-certificate")
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InovaGed", "SigningAgent");
        Directory.CreateDirectory(dataDir);
        var marker = Path.Combine(dataDir, "installation.json");
        if (command == "install")
        {
            var cert = LocalHttpsCertificate.CreateAndTrust();
            await File.WriteAllTextAsync(marker, $"{{\"installed\":true,\"protocol\":\"agent-cms-detached-v1\",\"httpsThumbprint\":\"{cert.Thumbprint}\"}}");
            Console.WriteLine("Signing Agent install: local HTTPS certificate initialized") ;
            return;
        }
        if (command == "rotate-certificate")
        {
            var cert = LocalHttpsCertificate.CreateAndTrust();
            await File.WriteAllTextAsync(marker, "{\"installed\":true,\"rotatedAtUtc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"httpsThumbprint\":\"" + cert.Thumbprint + "\"}");
            Console.WriteLine("Signing Agent rotate-certificate: local HTTPS profile rotated");
            return;
        }
        if (File.Exists(marker)) File.Delete(marker);
        LocalHttpsCertificate.RemoveAgentCertificates();
        Console.WriteLine("Signing Agent uninstall: local profile removed");
        return;
    }
    if (command is not "serve")
    {
        Console.Error.WriteLine("Usage: InovaGed.Signing.Agent [serve|doctor|version|install|uninstall|rotate-certificate]");
        Environment.ExitCode = 64;
        return;
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("SigningAgent"));
builder.Services.AddSingleton<PairingStore>();
builder.Services.AddSingleton<IAgentProtectedStorage, WindowsDpapiAgentProtectedStorage>();
builder.Services.AddSingleton<IAgentReplayProtectionService, AgentReplayProtectionService>();
builder.Services.AddSingleton<IAgentAuthenticationService, AgentAuthenticationService>();
builder.Services.AddHttpClient("signing-content").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<ISigningContentDownloader, SigningContentDownloader>();
builder.Services.AddSingleton<OperationStore>();
builder.Services.AddSingleton<CertificateStoreReader>();
builder.Services.AddCors(options => options.AddPolicy("agent", policy =>
{
    var origins = builder.Configuration.GetSection("SigningAgent:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

var urls = builder.Configuration["SigningAgent:Urls"] ?? "https://127.0.0.1:17891;https://[::1]:17891";
foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
    var uri = new Uri(url);
    if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address))
        throw new InvalidOperationException("O Signing Agent só pode escutar em 127.0.0.1 ou ::1.");
}
builder.WebHost.UseUrls(urls);

var app = builder.Build();
app.UseCors("agent");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", product = "InovaGed Signing Agent", loopbackOnly = true }));
app.MapGet("/info", () => Results.Ok(new { product = "InovaGed Signing Agent", protocol = "agent-cms-detached-v1", cmsDetached = true, conformity = "NOT_EVALUATED" }));
app.MapPost("/pairing/challenges", (PairingChallengeRequest request, PairingStore store) => store.CreateChallenge(request));
app.MapGet("/pairing/challenges/{id:guid}/confirm-ui", (Guid id, PairingStore store) => store.RenderChallenge(id));
app.MapPost("/pairing/challenges/{id:guid}/confirm-local", (Guid id, PairingStore store) => store.ConfirmLocal(id) ? Results.NoContent() : Results.NotFound());
app.MapPost("/pairing/challenges/{id:guid}/complete", (Guid id, PairingCompleteRequest request, PairingStore store) => store.Complete(id, request));
app.MapPost("/pair", (PairingRequest request, PairingStore store) => Results.Conflict(new { error = "pair_direct_token_disabled", next = "/pairing/challenges" }));
app.MapDelete("/pairings/{id:guid}", (Guid id, PairingStore store) => store.Revoke(id) ? Results.NoContent() : Results.NotFound());
app.MapDelete("/pairing", (string token, PairingStore store) => store.Revoke(token) ? Results.NoContent() : Results.NotFound());
app.MapGet("/certificates", (HttpRequest req, IAgentAuthenticationService auth, CertificateStoreReader reader) => auth.Authenticate(req).Success ? Results.Ok(reader.ListUsableCertificates()) : Results.Unauthorized());
app.MapPost("/operations", async (CmsSignRequest request, HttpRequest http, IAgentAuthenticationService auth, OperationStore operations, CertificateStoreReader certs, CancellationToken ct) =>
{
    if (!auth.Authenticate(http).Success) return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    var op = await operations.CreateAsync(request, certs, ct);
    return Results.Json(op.PublicView(), statusCode: StatusCodes.Status202Accepted);
});
app.MapGet("/operations/{id:guid}", (Guid id, HttpRequest req, IAgentAuthenticationService auth, OperationStore operations) => auth.Authenticate(req).Success && operations.TryGet(id, out var op) ? Results.Ok(op.PublicView()) : Results.NotFound());
app.MapGet("/operations/{id:guid}/confirm-ui", (Guid id, OperationStore operations) => operations.TryGet(id, out var op) ? Results.Content($"<html><body><h1>Confirmação local</h1><p>{System.Net.WebUtility.HtmlEncode(op.Request.DocumentName)}</p><p>{op.Request.Version}</p><p>{op.Request.ExpectedSha256}</p><p>{op.Request.Size}</p><p>{System.Net.WebUtility.HtmlEncode(op.Certificate.Name)}</p><p>{System.Net.WebUtility.HtmlEncode(op.Certificate.Issuer)}</p><p>{op.Certificate.MaskedCpf}</p><form method=\"post\" action=\"/operations/{id}/confirm-local\"><button type=\"submit\">Assinar</button></form></body></html>", "text/html") : Results.NotFound());
app.MapPost("/operations/{id:guid}/confirm-local", async (Guid id, OperationStore operations, CertificateStoreReader certs, CancellationToken ct) =>
    await operations.ConfirmAndSignAsync(id, certs, ct) is { } result ? Results.Ok(result) : Results.NotFound());
app.MapPost("/operations/{id:guid}/cancel", (Guid id, HttpRequest req, IAgentAuthenticationService auth, OperationStore operations) => auth.Authenticate(req).Success && operations.Cancel(id) ? Results.NoContent() : Results.NotFound());
app.Run();


public static class LocalHttpsCertificate
{
    private const string AgentOid = "1.3.6.1.4.1.55555.4.1.9";
    public static X509Certificate2 CreateAndTrust()
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=InovaGed Signing Agent Local HTTPS", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Parse("127.0.0.1")); san.AddIpAddress(IPAddress.IPv6Loopback); san.AddDnsName("localhost"); req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509Extension(new Oid(AgentOid), new byte[] { 5, 0 }, false));
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        var exportable = new X509Certificate2(cert.Export(X509ContentType.Pfx), string.Empty, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        using (var my = new X509Store(StoreName.My, StoreLocation.CurrentUser)) { my.Open(OpenFlags.ReadWrite); my.Add(exportable); }
        using (var root = new X509Store(StoreName.Root, StoreLocation.CurrentUser)) { root.Open(OpenFlags.ReadWrite); root.Add(new X509Certificate2(exportable.Export(X509ContentType.Cert))); }
        return exportable;
    }
    public static void RemoveAgentCertificates()
    {
        foreach (var location in new[] { StoreName.My, StoreName.Root })
        using (var store = new X509Store(location, StoreLocation.CurrentUser))
        { store.Open(OpenFlags.ReadWrite); foreach (var cert in store.Certificates.OfType<X509Certificate2>().Where(c => c.Subject.Contains("InovaGed Signing Agent Local HTTPS", StringComparison.Ordinal)).ToArray()) store.Remove(cert); }
    }
}


public sealed class AgentOptions { public string[] AllowedOrigins { get; set; } = []; public string[] AllowedServerHosts { get; set; } = []; public bool RequireHttps { get; set; } = true; public int MaxCmsMaterializationSizeMb { get; set; } = 64; }
public sealed record PairingRequest(string Origin, string Code);
public sealed record PairingChallengeRequest(string Origin);
public sealed record PairingCompleteRequest(string Code);
public sealed record PairingChallengeResponse(Guid ChallengeId, DateTimeOffset ExpiresAt, string ConfirmUiUrl, string Status);
public sealed record PairingResponse(Guid PairingId, string PairingToken, DateTimeOffset ExpiresAt, string Origin, string ProtocolVersion);
public sealed record CertificateInfo(string Thumbprint, string Name, string Issuer, string Serial, DateTimeOffset NotBefore, DateTimeOffset NotAfter, string Algorithm, string? MaskedCpf, bool HasPrivateKey, string KeyKind);
public sealed record CmsSignRequest(Uri ContentUrl, string ContentToken, string ExpectedSha256, string CertificateThumbprint, string DocumentName, string Version, long Size, string Purpose);
public sealed record CmsSignResult(Guid OperationId, string Status, string SignatureCmsBase64, string SignatureHashSha256, string CertificateDerBase64, CertificateInfo Certificate);

public sealed class PairingStore : IAgentPairingService
{
    private readonly IAgentProtectedStorage _storage;
    private readonly ConcurrentDictionary<Guid, (string Origin, string CodeHash, DateTimeOffset ExpiresAt, int Attempts, bool LocalApproved, bool Used, Guid? PairingId)> _challenges = new();
    private readonly ConcurrentDictionary<string, (Guid PairingId, string Origin, DateTimeOffset ExpiresAt, bool Revoked)> _tokens = new();
    public PairingStore(IAgentProtectedStorage storage)
    {
        _storage = storage;
        LoadPersistedPairingsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
    public PairingResponse Create(PairingRequest request) => throw new InvalidOperationException("pair_direct_token_disabled");
    public PairingChallengeResponse CreateChallenge(PairingChallengeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Origin) || request.Origin.Contains('*')) throw new InvalidOperationException("Origem inválida.");
        var id = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        _challenges[id] = (request.Origin, Hash(code), expires, 0, false, false, null);
        return new PairingChallengeResponse(id, expires, $"/pairing/challenges/{id}/confirm-ui", "PENDING_LOCAL_CONFIRMATION");
    }
    public IResult RenderChallenge(Guid id) => _challenges.TryGetValue(id, out var c) && c.ExpiresAt >= DateTimeOffset.UtcNow
        ? Results.Content($"<html><body><h1>Autorizar pairing InovaGed</h1><p>Origem: {System.Net.WebUtility.HtmlEncode(c.Origin)}</p><form method='post' action='/pairing/challenges/{id}/confirm-local'><button>Autorizar neste computador</button></form></body></html>", "text/html")
        : Results.NotFound();
    public bool ConfirmLocal(Guid id)
    {
        if (!_challenges.TryGetValue(id, out var c) || c.ExpiresAt < DateTimeOffset.UtcNow || c.Used) return false;
        _challenges[id] = (c.Origin, c.CodeHash, c.ExpiresAt, c.Attempts, true, c.Used, c.PairingId);
        return true;
    }
    public IResult Complete(Guid id, PairingCompleteRequest request)
    {
        if (!_challenges.TryGetValue(id, out var c) || c.ExpiresAt < DateTimeOffset.UtcNow || c.Used) return Results.NotFound();
        if (!c.LocalApproved) return Results.BadRequest(new { error = "local_approval_required" });
        if (c.Attempts >= 5) return Results.BadRequest(new { error = "attempts_exceeded" });
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(c.CodeHash), Convert.FromHexString(Hash(request.Code))))
        {
            _challenges[id] = (c.Origin, c.CodeHash, c.ExpiresAt, c.Attempts + 1, c.LocalApproved, c.Used, c.PairingId);
            return Results.BadRequest(new { error = "invalid_code", attemptsRemaining = Math.Max(0, 4 - c.Attempts) });
        }
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pairingId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddDays(30);
        _tokens[token] = (pairingId, c.Origin, expires, false);
        PersistPairingsAsync(CancellationToken.None).GetAwaiter().GetResult();
        _challenges[id] = (c.Origin, c.CodeHash, c.ExpiresAt, c.Attempts, c.LocalApproved, true, pairingId);
        return Results.Ok(new PairingResponse(pairingId, token, expires, c.Origin, "agent-cms-detached-v1"));
    }
    public Guid? GetPairingId(string token, string origin) => _tokens.TryGetValue(token, out var entry) && !entry.Revoked && entry.ExpiresAt >= DateTimeOffset.UtcNow && StringComparer.Ordinal.Equals(entry.Origin, origin) ? entry.PairingId : null;
    public bool IsValid(string token, string origin) => GetPairingId(token, origin).HasValue;
    public bool Revoke(string token) { var removed = _tokens.TryRemove(token, out _); if (removed) PersistPairingsAsync(CancellationToken.None).GetAwaiter().GetResult(); return removed; }
    public bool Revoke(Guid pairingId){ var changed=false; foreach(var kv in _tokens.Where(kv => kv.Value.PairingId == pairingId).ToArray()) changed |= _tokens.TryRemove(kv.Key, out _); if (changed) PersistPairingsAsync(CancellationToken.None).GetAwaiter().GetResult(); return true; }
    private async Task PersistPairingsAsync(CancellationToken ct)
    {
        var active = _tokens.Where(kv => !kv.Value.Revoked && kv.Value.ExpiresAt >= DateTimeOffset.UtcNow)
            .Select(kv => new PersistedPairing(kv.Value.PairingId, kv.Key, kv.Value.Origin, "agent-cms-detached-v1", DateTimeOffset.UtcNow, kv.Value.ExpiresAt, null)).ToArray();
        await _storage.SaveAsync("pairings", System.Text.Json.JsonSerializer.Serialize(active), ct);
    }
    private async Task LoadPersistedPairingsAsync(CancellationToken ct)
    {
        var json = await _storage.ReadAsync("pairings", ct);
        if (string.IsNullOrWhiteSpace(json)) return;
        foreach (var p in System.Text.Json.JsonSerializer.Deserialize<PersistedPairing[]>(json) ?? [])
            if (p.ExpiresAt >= DateTimeOffset.UtcNow && p.RevokedAt is null) _tokens[p.Token] = (p.PairingId, p.Origin, p.ExpiresAt, false);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
public sealed record PersistedPairing(Guid PairingId, string Token, string Origin, string ProtocolVersion, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);

public sealed class WindowsDpapiAgentProtectedStorage : IAgentProtectedStorage
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("InovaGed.Signing.Agent.v1");
    private readonly string _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InovaGed", "SigningAgent", "protected");
    public async Task SaveAsync(string name, string value, CancellationToken ct)
    {
        Directory.CreateDirectory(_dir);
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var protectedBytes = OperatingSystem.IsWindows() ? ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser) : bytes;
        await File.WriteAllBytesAsync(Path.Combine(_dir, name + ".protected"), protectedBytes, ct);
    }
    public async Task<string?> ReadAsync(string name, CancellationToken ct)
    {
        var path=Path.Combine(_dir, name + ".protected");
        if(!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, ct);
        var bytes = OperatingSystem.IsWindows() ? ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser) : protectedBytes;
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
    public Task DeleteAsync(string name, CancellationToken ct){ var path=Path.Combine(_dir, name + ".protected"); if(File.Exists(path)) File.Delete(path); return Task.CompletedTask; }
}

public sealed class TestAgentProtectedStorage : IAgentProtectedStorage
{
    private readonly ConcurrentDictionary<string,string> _values = new();
    public Task SaveAsync(string name, string value, CancellationToken ct){ _values[name]=value; return Task.CompletedTask; }
    public Task<string?> ReadAsync(string name, CancellationToken ct)=>Task.FromResult(_values.TryGetValue(name, out var value) ? value : null);
    public Task DeleteAsync(string name, CancellationToken ct){ _values.TryRemove(name, out _); return Task.CompletedTask; }
}

public sealed class OperationStore(ISigningContentDownloader downloader, Microsoft.Extensions.Options.IOptions<AgentOptions> options)
{
    private readonly ConcurrentDictionary<Guid, SigningOperation> _operations = new();
    public async Task<SigningOperation> CreateAsync(CmsSignRequest request, CertificateStoreReader certs, CancellationToken ct)
    {
        var cert = certs.Find(request.CertificateThumbprint) ?? throw new InvalidOperationException("Certificado não encontrado.");
        var op = new SigningOperation(Guid.NewGuid(), request, certs.ToInfo(cert), "WAITING_CONFIRMATION", null, DateTimeOffset.UtcNow.AddMinutes(3));
        _operations[op.Id] = op;
        await Task.CompletedTask;
        return op;
    }
    public bool TryGet(Guid id, out SigningOperation? op) => _operations.TryGetValue(id, out op);
    public bool Cancel(Guid id) => _operations.TryUpdate(id, _operations[id] with { Status = "CANCELLED" }, _operations[id]);
    public async Task<CmsSignResult?> ConfirmAndSignAsync(Guid id, CertificateStoreReader certs, CancellationToken ct)
    {
        if (!_operations.TryGetValue(id, out var op) || op.Status != "WAITING_CONFIRMATION" || op.ExpiresAt < DateTimeOffset.UtcNow) return null;
        await using var downloaded = await downloader.DownloadAsync(op.Request.ContentUrl, op.Request.ContentToken, op.Request.Size, op.Request.ExpectedSha256, ct);
        await using var contentStream = downloaded.OpenRead();
        var maxBytes = (long)Math.Max(1, options.Value.MaxCmsMaterializationSizeMb) * 1024 * 1024;
        if (downloaded.Size > maxBytes) throw new InvalidOperationException("CMS_MATERIALIZATION_LIMIT_EXCEEDED");
        using var ms = new MemoryStream();
        await contentStream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var cert = certs.Find(op.Request.CertificateThumbprint) ?? throw new InvalidOperationException("Certificado indisponível.");
        var cms = new SignedCms(new ContentInfo(bytes), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert) { IncludeOption = X509IncludeOption.ExcludeRoot };
        signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
        cms.ComputeSignature(signer, silent: false);
        var signature = cms.Encode();
        var result = new CmsSignResult(id, "SIGNED", Convert.ToBase64String(signature), Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant(), Convert.ToBase64String(cert.Export(X509ContentType.Cert)), op.Certificate);
        _operations[id] = op with { Status = "SIGNED", Result = result };
        return result;
    }
}
public sealed record SigningOperation(Guid Id, CmsSignRequest Request, CertificateInfo Certificate, string Status, CmsSignResult? Result, DateTimeOffset ExpiresAt)
{ public object PublicView() => new { Id, Status, Request.DocumentName, Request.Version, Request.ExpectedSha256, Request.Size, Request.Purpose, Certificate, ExpiresAt }; }

public sealed class CertificateStoreReader : ILocalCertificateStore
{
    public IReadOnlyList<CertificateInfo> ListUsableCertificates() => Open().Select(ToInfo).ToList();
    public X509Certificate2? Find(string thumbprint) => Open().FirstOrDefault(c => string.Equals(Normalize(c.Thumbprint), Normalize(thumbprint), StringComparison.OrdinalIgnoreCase));
    private static IEnumerable<X509Certificate2> Open()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadOnly);
        return store.Certificates.OfType<X509Certificate2>().Where(c => c.HasPrivateKey && DateTimeOffset.UtcNow >= c.NotBefore && DateTimeOffset.UtcNow <= c.NotAfter && AllowsDigitalSignature(c)).ToArray();
    }
    public CertificateInfo ToInfo(X509Certificate2 c) => new(Normalize(c.Thumbprint), c.GetNameInfo(X509NameType.SimpleName, false), c.Issuer, c.SerialNumber, c.NotBefore, c.NotAfter, c.PublicKey.Oid.FriendlyName ?? c.PublicKey.Oid.Value ?? "unknown", MaskCpf(c.Subject), c.HasPrivateKey, "UNKNOWN");
    private static bool AllowsDigitalSignature(X509Certificate2 c) => c.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is not { } ku || ku.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature);
    private static string Normalize(string? s) => (s ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
    private static string? MaskCpf(string subject) { var digits = new string(subject.Where(char.IsDigit).ToArray()); return digits.Length >= 11 ? $"***.{digits[^8..^5]}.{digits[^5..^2]}-**" : null; }
}
