using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Signatures;

public sealed class DigitalSignatureOptions
{
    public bool Enabled { get; set; }
    [Required] public string Mode { get; set; } = "AgentCms";
    [Range(30, 3600)] public int SessionTtlSeconds { get; set; } = 300;
    [Range(30, 600)] public int ContentTokenTtlSeconds { get; set; } = 120;
    [Range(30, 3600)] public int CompletionTokenTtlSeconds { get; set; } = 300;
    [Range(1, 1024)] public int MaxDocumentSizeMb { get; set; } = 100;
    [Required] public string HashAlgorithm { get; set; } = "SHA256";
    public bool RequireCertificateIdentityMatch { get; set; } = true;
    public string PublicServerBaseUrl { get; set; } = "https://gedophir.sistemasld.com.br";
    public string? CertificateIdentityHmacKey { get; set; }
    public string? CertificateIdentityHmacKeyVersion { get; set; }
    public bool AllowServerSidePfxUpload { get; set; }
    public bool AllowInternalTestCertificates { get; set; }
    public bool RequireLoopbackHttps { get; set; } = true;
    [Required] public string AgentBaseUrl { get; set; } = "https://127.0.0.1:17891";
    public string[] AllowedAgentOrigins { get; set; } = [];
    public DigitalSignatureAgentOptions Agent { get; set; } = new();
}

public sealed class DigitalSignatureAgentOptions
{
    [Required] public string BaseUrl { get; set; } = "https://127.0.0.1:17891";
    public bool RequireHttps { get; set; } = true;
    public string[] AllowedOrigins { get; set; } = [];
    public string[] AllowedServerHosts { get; set; } = [];
    [Range(30, 3600)] public int PairingTtlSeconds { get; set; } = 300;
    [Range(30, 3600)] public int OperationTtlSeconds { get; set; } = 600;
}
