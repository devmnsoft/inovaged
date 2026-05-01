using System.Security.Claims;
using InovaGed.Application.Auth;
using InovaGed.Web.Models.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IAuthRepository _repo;
    private static readonly PasswordHasher<object> _hasher = new();

    public AccountController(IAuthRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginVM { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var tenantCode = (vm.TenantSlug ?? string.Empty).Trim().ToLowerInvariant();
        var loginOrCpf = (vm.Email ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(tenantCode) || string.IsNullOrWhiteSpace(loginOrCpf))
        {
            ModelState.AddModelError("", "Informe o e-mail ou CPF e a senha.");
            return View(vm);
        }

        var user = await _repo.FindUserAsync(tenantCode, loginOrCpf, ct);

        if (user is null)
        {
            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var correlationId = HttpContext.TraceIdentifier;

        if (!user.IsActive)
        {
            await _repo.RegisterLoginFailureAsync(
                user.TenantId,
                user.UserId,
                "Usuário inativo.",
                ip,
                userAgent,
                correlationId,
                ct);

            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        if (user.IsLocked && user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError("", "Usuário temporariamente bloqueado. Tente novamente mais tarde.");
            return View(vm);
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            await _repo.RegisterLoginFailureAsync(
                user.TenantId,
                user.UserId,
                "Usuário sem hash de senha.",
                ip,
                userAgent,
                correlationId,
                ct);

            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        var verify = _hasher.VerifyHashedPassword(
            user: null!,
            hashedPassword: user.PasswordHash,
            providedPassword: vm.Password ?? string.Empty);

        if (verify == PasswordVerificationResult.Failed)
        {
            await _repo.RegisterLoginFailureAsync(
                user.TenantId,
                user.UserId,
                "Senha inválida.",
                ip,
                userAgent,
                correlationId,
                ct);

            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        await _repo.RegisterLoginSuccessAsync(
            user.TenantId,
            user.UserId,
            ip,
            userAgent,
            correlationId,
            ct);

        var roles = await _repo.GetRolesAsync(user.TenantId, user.UserId, ct);

        var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new(ClaimTypes.Name, user.Name),
        new(ClaimTypes.Email, user.Email),
        new("tenant_id", user.TenantId.ToString()),
        new("tenant_code", tenantCode),
        new("security_level", user.SecurityLevel ?? "PUBLIC"),
        new("mfa_enabled", user.MfaEnabled.ToString()),
        new("certificate_required", user.CertificateRequired.ToString()),
        new("can_sign_with_icp", user.CanSignWithIcp.ToString())
    };

        if (user.ServidorId.HasValue)
        {
            claims.Add(new Claim("servidor_id", user.ServidorId.Value.ToString()));
        }

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true
            });

        if (user.MustChangePassword)
        {
            return RedirectToAction("ChangePassword", "Account");
        }

        if (!string.IsNullOrWhiteSpace(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
        {
            return Redirect(vm.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        TempData["Error"] = "A troca de senha será implementada no próximo passo do módulo de recuperação de senha.";
        return View();
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordVM
        {
            TenantSlug = "default"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPasswordBuscar(ForgotPasswordVM vm, CancellationToken ct)
    {
        vm.TenantSlug = string.IsNullOrWhiteSpace(vm.TenantSlug)
            ? "default"
            : vm.TenantSlug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(vm.Cpf))
        {
            ModelState.AddModelError(nameof(vm.Cpf), "Informe o CPF.");
            return View("ForgotPassword", vm);
        }

        var usuario = await _repo.FindUserForPasswordRecoveryByCpfAsync(
            vm.TenantSlug,
            vm.Cpf,
            ct);

        if (usuario is null)
        {
            vm.UsuarioEncontrado = false;
            ModelState.AddModelError("", "Nenhum usuário foi encontrado para o CPF informado.");
            return View("ForgotPassword", vm);
        }

        if (!usuario.IsActive)
        {
            vm.UsuarioEncontrado = false;
            ModelState.AddModelError("", "O usuário vinculado a este CPF está inativo.");
            return View("ForgotPassword", vm);
        }

        vm.UsuarioEncontrado = true;
        vm.TenantId = usuario.TenantId;
        vm.UserId = usuario.UserId;
        vm.NomeUsuario = usuario.NomeUsuario;
        vm.EmailUsuario = usuario.EmailUsuario;

        return View("ForgotPassword", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPasswordRedefinir(ForgotPasswordVM vm, CancellationToken ct)
    {
        vm.TenantSlug = string.IsNullOrWhiteSpace(vm.TenantSlug)
            ? "default"
            : vm.TenantSlug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(vm.Cpf))
        {
            ModelState.AddModelError(nameof(vm.Cpf), "Informe o CPF.");
            return View("ForgotPassword", vm);
        }

        var usuario = await _repo.FindUserForPasswordRecoveryByCpfAsync(
            vm.TenantSlug,
            vm.Cpf,
            ct);

        if (usuario is null)
        {
            ModelState.AddModelError("", "Nenhum usuário foi encontrado para o CPF informado.");
            return View("ForgotPassword", vm);
        }

        vm.UsuarioEncontrado = true;
        vm.TenantId = usuario.TenantId;
        vm.UserId = usuario.UserId;
        vm.NomeUsuario = usuario.NomeUsuario;
        vm.EmailUsuario = usuario.EmailUsuario;

        if (string.IsNullOrWhiteSpace(vm.NovaSenha))
        {
            ModelState.AddModelError(nameof(vm.NovaSenha), "Informe a nova senha.");
            return View("ForgotPassword", vm);
        }

        if (vm.NovaSenha.Length < 4)
        {
            ModelState.AddModelError(nameof(vm.NovaSenha), "A nova senha deve possuir no mínimo 4 caracteres.");
            return View("ForgotPassword", vm);
        }

        if (vm.NovaSenha != vm.ConfirmarNovaSenha)
        {
            ModelState.AddModelError(nameof(vm.ConfirmarNovaSenha), "A confirmação da senha não confere.");
            return View("ForgotPassword", vm);
        }

        var hasher = new PasswordHasher<object>();
        var newHash = hasher.HashPassword(null!, vm.NovaSenha);

        await _repo.ResetPasswordByUserIdAsync(
            usuario.TenantId,
            usuario.UserId,
            newHash,
            ct);

        TempData["Success"] = "Senha redefinida com sucesso. Acesse o sistema com a nova senha.";

        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ForgotPassword(string tenantCode, string email)
    {
        TempData["Success"] = "Se os dados estiverem corretos, as instruções de recuperação serão enviadas para o e-mail informado.";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}