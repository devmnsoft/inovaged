namespace InovaGed.Application.Security
{
    public sealed class CertificateValidationResult
    {
        public bool Ok { get; init; }
        public string? Error { get; init; }
        public string? ExtractedCpf { get; init; }
        public string? Thumbprint { get; init; }
    }
}
