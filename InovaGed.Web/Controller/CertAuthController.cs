using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using InovaGed.Application.Security;
using InovaGed.Application.Security.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[AllowAnonymous]
public sealed class CertAuthController : Controller
{
    private readonly ILogger<CertAuthController> _logger;
    private readonly IAppUserRepository _users;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;

    private static readonly Guid TenantPoC = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public CertAuthController(
        ILogger<CertAuthController> logger,
        IAppUserRepository users,
        IWebHostEnvironment env,
        IConfiguration cfg)
    {
        _logger = logger;
        _users = users;
        _env = env;
        _cfg = cfg;
    }

    // ✅ você pode manter o GET /auth/cert, mas o fluxo principal será via /Account/Login
    [HttpGet("/auth/cert")]
    public IActionResult Index()
        => RedirectToAction("Login", "Account");

    private IActionResult BackToLoginWithCertError(string msg, string? returnUrl = null)
    {
        TempData["cert_err"] = msg;
        TempData["open_cert"] = true;
        return RedirectToAction("Login", "Account", new { returnUrl });
    }

    [HttpPost("/auth/cert")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(CertLoginRequest req, CancellationToken ct)
    {
        var cpfUser = CpfFromCertificate.NormalizeCpf(req.UserCpf);

        if (string.IsNullOrWhiteSpace(cpfUser) || cpfUser.Length != 11)
            return BackToLoginWithCertError("CPF inválido. Informe 11 dígitos.", req.ReturnUrl);

        if (string.IsNullOrWhiteSpace(req.CertificateBase64))
            return BackToLoginWithCertError("Selecione um certificado (.cer ou .pfx).", req.ReturnUrl);

        X509Certificate2 cert;
        try
        {
            var raw = Convert.FromBase64String(req.CertificateBase64);
            cert = string.IsNullOrWhiteSpace(req.PfxPassword)
                ? new X509Certificate2(raw)
                : new X509Certificate2(raw, req.PfxPassword,
                    X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CertAuth: erro carregando certificado.");
            return BackToLoginWithCertError("Certificado inválido (arquivo/senha/formato).", req.ReturnUrl);
        }

        // ✅ Flag de PoC (não depende do ambiente)
        var allowTestSelfSigned =
            _cfg.GetValue<bool>("Auth:AllowTestCertificates") || _env.IsDevelopment();

        _logger.LogWarning("CertAuth: env={Env} allowTest={AllowTest} subject={Subject}",
            _env.EnvironmentName, allowTestSelfSigned, cert.Subject);

        var v = CertificateValidator.Validate(cert, allowTestSelfSigned);

        if (!v.Ok)
            return BackToLoginWithCertError(v.Error ?? "Falha na validação do certificado.", req.ReturnUrl);

        if (!string.Equals(cpfUser, v.ExtractedCpf, StringComparison.Ordinal))
            return BackToLoginWithCertError("CPF informado não corresponde ao CPF do certificado.", req.ReturnUrl);

        var user = await _users.GetByCpfAsync(TenantPoC, cpfUser, ct);
        if (user is null) return BackToLoginWithCertError("Usuário não encontrado para o CPF informado.", req.ReturnUrl);
        if (!user.IsActive) return BackToLoginWithCertError("Usuário inativo.", req.ReturnUrl);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new("tenant_id", user.TenantId.ToString()),
            new("auth_method", "icp_cert_poc"),
            new("cpf", cpfUser),
            new("cert_thumbprint", v.Thumbprint ?? "")
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        TempData["cert_ok"] = "Autenticação por certificado realizada com sucesso.";
        return RedirectToAction("Index", "Home");
    }
}