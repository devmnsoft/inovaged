namespace InovaGed.Application.Security
{
    public sealed class CertLoginRequest
    {
        // PoC: pode ser .cer (base64 DER) ou .pfx (base64) + senha
        public string CertificateBase64 { get; set; } = "";
        public string? PfxPassword { get; set; }

        // PoC: CPF do usuário que está tentando logar (ou a gente busca por email/login)
        public string UserCpf { get; set; } = "";
        public string? ReturnUrl { get; set; }

    }
}
