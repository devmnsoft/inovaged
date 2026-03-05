using System.Security.Cryptography.X509Certificates;

namespace InovaGed.Application.Security;

public static class CertificateValidator
{
    public static CertificateValidationResult Validate(X509Certificate2 cert, bool allowTestSelfSigned)
    {
        var now = DateTimeOffset.UtcNow;

        if (now < cert.NotBefore.ToUniversalTime())
            return new() { Ok = false, Error = "Certificado ainda não é válido (NotBefore)." };

        if (now > cert.NotAfter.ToUniversalTime())
            return new() { Ok = false, Error = "Certificado expirado (NotAfter)." };

        using var chain = new X509Chain();

        // ✅ Em PoC/teste, NÃO checar revogação online (autoassinado não tem OCSP/CRL)
        if (allowTestSelfSigned)
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        }
        else
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(8);
        }

        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        var ok = chain.Build(cert);

        if (!ok)
        {
            var reasons = string.Join(" | ", chain.ChainStatus.Select(s =>
                $"{s.Status}({(s.StatusInformation ?? "").Trim()})"));

            if (allowTestSelfSigned)
            {
                // ✅ Em DEV/PoC aceita os status comuns de certificado de teste
                var onlyAllowed = chain.ChainStatus.All(s =>
                    s.Status == X509ChainStatusFlags.UntrustedRoot ||
                    s.Status == X509ChainStatusFlags.PartialChain ||
                    s.Status == X509ChainStatusFlags.RevocationStatusUnknown ||
                    s.Status == X509ChainStatusFlags.OfflineRevocation);

                if (!onlyAllowed)
                    return new() { Ok = false, Error = $"Falha na cadeia (PoC): {reasons}" };

                // se só veio coisa “esperada” em teste, segue
            }
            else
            {
                return new() { Ok = false, Error = $"Falha na cadeia/revogação: {reasons}" };
            }
        }

        var extractedCpf = CpfFromCertificate.TryExtractCpf(cert);
        var cpf = CpfFromCertificate.NormalizeCpf(extractedCpf ?? "");

        if (cpf.Length != 11)
            return new() { Ok = false, Error = "Não foi possível extrair CPF do certificado." };

        return new()
        {
            Ok = true,
            ExtractedCpf = cpf,
            Thumbprint = cert.Thumbprint
        };
    }
}