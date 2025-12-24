using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Auth;
using InovaGed.Web.Models.Auth;

namespace InovaGed.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IAuthRepository _repo;

    // PasswordHasher compatível com hashes "AQAAAA..."
    private static readonly PasswordHasher<object> _hasher = new();

    public AccountController(IAuthRepository repo) => _repo = repo;

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginVM { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        var tenantCode = (vm.TenantSlug ?? string.Empty).Trim().ToLowerInvariant(); // na prática: tenant.code
        var email = (vm.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(tenantCode) || string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        var user = await _repo.FindUserAsync(tenantCode, email, ct);
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        // ✅ Valida hash do ASP.NET Identity (AQAAAA...)
        var verify = _hasher.VerifyHashedPassword(
            user: null!,
            hashedPassword: user.PasswordHash,
            providedPassword: vm.Password ?? string.Empty);

        if (verify == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        // (Opcional) Se quiser rehash automático, crie método no repo e descomente:
        // if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        // {
        //     var newHash = _hasher.HashPassword(null!, vm.Password ?? string.Empty);
        //     await _repo.UpdatePasswordHashAsync(user.TenantId, user.UserId, newHash, ct);
        // }

        var roles = await _repo.GetRolesAsync(user.TenantId, user.UserId, ct);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),

            new("tenant_id", user.TenantId.ToString()),
            new("tenant_code", tenantCode), // mais correto que "tenant_slug"
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true
            });

        if (!string.IsNullOrWhiteSpace(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
            return Redirect(vm.ReturnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
