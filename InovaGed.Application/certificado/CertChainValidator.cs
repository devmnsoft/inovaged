using System.Security.Cryptography.X509Certificates;

namespace InovaGed.Application.certificado
{
    public static class CertChainValidator
    {
        public static (bool ok, string details) ValidateChain(X509Certificate2 cert)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online; // para PoC, online
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            var ok = chain.Build(cert);
            if (ok) return (true, "CHAIN_OK");

            var msg = string.Join(" | ", chain.ChainStatus.Select(s => $"{s.Status}:{s.StatusInformation}".Trim()));
            return (false, msg);
        }
    }
}
