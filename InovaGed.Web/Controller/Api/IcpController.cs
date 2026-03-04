using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

 
public sealed class IcpController : Controller
{
    public IActionResult Index()
    {
        var cert = HttpContext.Connection.ClientCertificate;

        var vm = new IcpEvidenceVM
        {
            HasCertificate = cert != null,
            Subject = cert?.Subject,
            Issuer = cert?.Issuer,
            NotBefore = cert?.NotBefore,
            NotAfter = cert?.NotAfter,
            Thumbprint = cert?.Thumbprint,
            CpfFromCert = cert != null ? TryGetCpf(cert) : null,
            ChainOk = cert != null && ValidateChain(cert, out var chainMsg)
        };

        return View(vm);
    }

    private static string? TryGetCpf(X509Certificate2 cert)
    {
        // ICP-Brasil: CPF costuma estar no OID 2.16.76.1.3.1 ou no Subject
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value == "2.16.76.1.3.1")
            {
                // Algumas ACs colocam CPF como ASN1 string; aqui é evidência.
                // Para produção, parse ASN.1 corretamente.
                return ext.Format(false);
            }
        }

        var subject = cert.Subject ?? "";
        // fallback simples: procure "CPF="
        var idx = subject.IndexOf("CPF=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var s = subject.Substring(idx + 4);
            var end = s.IndexOf(',');
            return (end >= 0 ? s[..end] : s).Trim();
        }

        return null;
    }

    private static bool ValidateChain(X509Certificate2 cert, out string message)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);

        var ok = chain.Build(cert);
        if (ok)
        {
            message = "Cadeia válida (revogação online habilitada).";
            return true;
        }

        var errs = chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation}".Trim());
        message = "Cadeia inválida: " + string.Join(" | ", errs);
        return false;
    }

    public sealed class IcpEvidenceVM
    {
        public bool HasCertificate { get; set; }
        public string? Subject { get; set; }
        public string? Issuer { get; set; }
        public DateTime? NotBefore { get; set; }
        public DateTime? NotAfter { get; set; }
        public string? Thumbprint { get; set; }
        public string? CpfFromCert { get; set; }
        public bool ChainOk { get; set; }
        public string? ChainMessage { get; set; }
    }
}