using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace InovaGed.Application.Security
{
    public static class CertificateValidator
    {
        public static CertificateValidationResult Validate(X509Certificate2 cert, bool allowTestSelfSigned)
        {
            // Vigência (mantém igual)
            var now = DateTimeOffset.UtcNow;
            if (now < cert.NotBefore.ToUniversalTime())
                return new() { Ok = false, Error = "Certificado ainda não é válido (NotBefore)." };
            if (now > cert.NotAfter.ToUniversalTime())
                return new() { Ok = false, Error = "Certificado expirado (NotAfter)." };

            using var chain = new X509Chain();

            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);

            var ok = chain.Build(cert);

            if (!ok)
            {
                // ✅ MODO TESTE: aceita autoassinado SOMENTE quando habilitado
                // Autoassinado geralmente dá UntrustedRoot / PartialChain
                if (allowTestSelfSigned)
                {
                    var allowed = chain.ChainStatus.All(s =>
                        s.Status == X509ChainStatusFlags.UntrustedRoot ||
                        s.Status == X509ChainStatusFlags.PartialChain);

                    if (!allowed)
                    {
                        var reasons = string.Join(" | ", chain.ChainStatus.Select(s => $"{s.Status}({(s.StatusInformation ?? "").Trim()})"));
                        return new() { Ok = false, Error = $"Falha na cadeia (teste): {reasons}" };
                    }
                }
                else
                {
                    var reasons = string.Join(" | ", chain.ChainStatus.Select(s => $"{s.Status}({(s.StatusInformation ?? "").Trim()})"));
                    return new() { Ok = false, Error = $"Falha na cadeia/revogação: {reasons}" };
                }
            }

            var extractedCpf = CpfFromCertificate.TryExtractCpf(cert);
            if (string.IsNullOrWhiteSpace(extractedCpf))
                return new() { Ok = false, Error = "Não foi possível extrair CPF do certificado." };

            return new()
            {
                Ok = true,
                ExtractedCpf = CpfFromCertificate.NormalizeCpf(extractedCpf),
                Thumbprint = cert.Thumbprint
            };
        }
    }
}
