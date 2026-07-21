using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Signatures;

public sealed class DigitalSignatureOptions
{
    public bool Enabled { get; set; }
    [Required] public string Mode { get; set; } = "AgentCms";
    public bool AllowServerSidePfxUpload { get; set; }
    [Range(30, 3600)] public int SessionTtlSeconds { get; set; } = 300;
    [Range(30, 600)] public int ContentTokenTtlSeconds { get; set; } = 120;
    [Range(1, 1024)] public int MaxDocumentSizeMb { get; set; } = 100;
    public bool RequireLoopbackHttps { get; set; } = true;
    [Required] public string AgentBaseUrl { get; set; } = "https://127.0.0.1:17891";
    public string[] AllowedAgentOrigins { get; set; } = [];
    public bool AllowInternalTestCertificates { get; set; }
}
