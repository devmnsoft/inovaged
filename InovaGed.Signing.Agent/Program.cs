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
            await File.WriteAllTextAsync(marker, "{\"installed\":true,\"protocol\":\"agent-cms-detached-v1\"}");
            Console.WriteLine("Signing Agent install: local profile initialized");
            return;
        }
        if (command == "rotate-certificate")
        {
            await File.WriteAllTextAsync(marker, "{\"installed\":true,\"rotatedAtUtc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\"}");
            Console.WriteLine("Signing Agent rotate-certificate: local profile rotated");
            return;
        }
        if (File.Exists(marker)) File.Delete(marker);
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
app.MapPost("/pair", (PairingRequest request, PairingStore store) => store.Create(request));
app.MapDelete("/pairing", (string token, PairingStore store) => store.Revoke(token) ? Results.NoContent() : Results.NotFound());
app.MapGet("/certificates", (HttpRequest req, IAgentAuthenticationService auth, CertificateStoreReader reader) => auth.Authenticate(req) ? Results.Ok(reader.ListUsableCertificates()) : Results.Unauthorized());
app.MapPost("/operations", async (CmsSignRequest request, HttpRequest http, IAgentAuthenticationService auth, OperationStore operations, CertificateStoreReader certs, CancellationToken ct) =>
{
    if (!auth.Authenticate(http)) return Results.Unauthorized();
    var op = await operations.CreateAsync(request, certs, ct);
    return Results.Accepted($"/operations/{op.Id}", op.PublicView());
});
app.MapGet("/operations/{id:guid}", (Guid id, HttpRequest req, IAgentAuthenticationService auth, OperationStore operations) => auth.Authenticate(req) && operations.TryGet(id, out var op) ? Results.Ok(op.PublicView()) : Results.NotFound());
app.MapGet("/operations/{id:guid}/confirm-ui", (Guid id, OperationStore operations) => operations.TryGet(id, out var op) ? Results.Content($"<html><body><h1>Confirmação local</h1><p>{System.Net.WebUtility.HtmlEncode(op.Request.DocumentName)}</p><p>{op.Request.Version}</p><p>{op.Request.ExpectedSha256}</p><p>{op.Request.Size}</p><p>{System.Net.WebUtility.HtmlEncode(op.Certificate.Name)}</p><p>{System.Net.WebUtility.HtmlEncode(op.Certificate.Issuer)}</p><p>{op.Certificate.MaskedCpf}</p><form method=\"post\" action=\"/operations/{id}/confirm-local\"><button type=\"submit\">Assinar</button></form></body></html>", "text/html") : Results.NotFound());
app.MapPost("/operations/{id:guid}/confirm-local", async (Guid id, OperationStore operations, CertificateStoreReader certs, CancellationToken ct) =>
    await operations.ConfirmAndSignAsync(id, certs, ct) is { } result ? Results.Ok(result) : Results.NotFound());
app.MapPost("/operations/{id:guid}/cancel", (Guid id, HttpRequest req, IAgentAuthenticationService auth, OperationStore operations) => auth.Authenticate(req) && operations.Cancel(id) ? Results.NoContent() : Results.NotFound());
app.Run();

public sealed class AgentOptions { public string[] AllowedOrigins { get; set; } = []; public string[] AllowedServerHosts { get; set; } = []; public bool RequireHttps { get; set; } = true; }
public sealed record PairingRequest(string Origin, string Code);
public sealed record PairingResponse(string PairingToken, DateTimeOffset ExpiresAt);
public sealed record CertificateInfo(string Thumbprint, string Name, string Issuer, string Serial, DateTimeOffset NotBefore, DateTimeOffset NotAfter, string Algorithm, string? MaskedCpf, bool HasPrivateKey, string KeyKind);
public sealed record CmsSignRequest(Uri ContentUrl, string ContentToken, string ExpectedSha256, string CertificateThumbprint, string DocumentName, string Version, long Size, string Purpose);
public sealed record CmsSignResult(Guid OperationId, string Status, string SignatureCmsBase64, string SignatureHashSha256, string CertificateDerBase64, CertificateInfo Certificate);

public sealed class PairingStore : IAgentPairingService
{
    private readonly ConcurrentDictionary<string, (string Origin, DateTimeOffset ExpiresAt, bool Revoked)> _tokens = new();
    public PairingResponse Create(PairingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Origin) || request.Origin.Contains('*')) throw new InvalidOperationException("Origem inválida.");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        _tokens[token] = (request.Origin, expires, false);
        return new PairingResponse(token, expires);
    }
    public bool IsValid(string token, string origin)
    {
        return _tokens.TryGetValue(token, out var entry) && !entry.Revoked && entry.ExpiresAt >= DateTimeOffset.UtcNow && StringComparer.Ordinal.Equals(entry.Origin, origin);
    }
    public bool Revoke(string token) => _tokens.TryRemove(token, out _);
}

public sealed class OperationStore(ISigningContentDownloader downloader)
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
