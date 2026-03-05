using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

public static class CpfFromCertificate
{
    public static string? TryExtractCpf(X509Certificate2 cert)
    {
        // tenta subject (alguns trazem CPF=..., SERIALNUMBER=..., etc.)
        var subject = cert.Subject ?? "";
        var m1 = Regex.Match(subject, @"(\d{11})");
        if (m1.Success) return m1.Groups[1].Value;

        // tenta texto das extensões (formato humano)
        foreach (var ext in cert.Extensions)
        {
            var s = ext.Format(true);
            var m2 = Regex.Match(s ?? "", @"(\d{11})");
            if (m2.Success) return m2.Groups[1].Value;
        }

        return null;
    }

    public static string NormalizeCpf(string cpf)
        => Regex.Replace(cpf ?? "", @"\D", "");
}