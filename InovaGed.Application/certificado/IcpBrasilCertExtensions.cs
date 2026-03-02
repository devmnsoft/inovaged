using System.Security.Cryptography.X509Certificates;

namespace InovaGed.Application.certificado
{
    public static class IcpBrasilCertExtensions
    {
        public static string? TryGetCpf(this X509Certificate2 cert)
        {
            foreach (var ext in cert.Extensions)
            {
                // ICP-Brasil CPF geralmente vem em extension (Subject Alternative Name / otherName),
                // mas na prática varia. Para PoC, você aceita:
                // - OID 2.16.76.1.3.1 em alguma extensão ASN.1
                // - ou CPF no Subject (fallback)
                if (ext.Oid?.Value == "2.16.76.1.3.1")
                {
                    // muitas libs precisariam decodificar ASN.1.
                    // Para PoC, você guarda o RawData e extrai por heurística simples,
                    // e depois refina com BouncyCastle.
                    var raw = ext.RawData;
                    var s = BitConverter.ToString(raw).Replace("-", "");
                    // heurística: procura sequências numéricas grandes e filtra 11 dígitos na pipeline (refine depois)
                }
            }

            // fallback: tenta achar 11 dígitos no Subject
            var digits = new string(cert.Subject.Where(char.IsDigit).ToArray());
            if (digits.Length >= 11)
            {
                // pega o último bloco de 11 como fallback
                return digits.Substring(digits.Length - 11, 11);
            }
            return null;
        }
    }
}
