namespace InovaGed.Application.certificado
{
    public static class CpfRules
    {
        public static bool EqualsCpf(string? cpfUser, string? cpfCert)
        {
            string Norm(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());
            return !string.IsNullOrWhiteSpace(cpfUser)
                   && !string.IsNullOrWhiteSpace(cpfCert)
                   && Norm(cpfUser) == Norm(cpfCert);
        }
    }
}
