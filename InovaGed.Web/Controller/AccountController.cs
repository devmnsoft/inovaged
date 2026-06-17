using System.Security.Claims;
using InovaGed.Application.Auth;
using InovaGed.Web.Models.Auth;
using InovaGed.Web.Security;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Security;
using InovaGed.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IAuthRepository _repo;
    private readonly IAuditWriter _audit;
    private readonly ILogger<AccountController> _logger;
    private readonly IGedAccessPolicyService _accessPolicy;
    private static readonly PasswordHasher<ApplicationUser> _hasher = new();

    public AccountController(IAuthRepository repo, IAuditWriter audit, ILogger<AccountController> logger, IGedAccessPolicyService accessPolicy)
    {
        _repo = repo;
        _audit = audit;
        _logger = logger;
        _accessPolicy = accessPolicy;
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
        try
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
                await _audit.WriteAsync(
                    Guid.Empty, null, "LOGIN_FAILURE", "auth", null, "Credenciais inválidas: usuário não encontrado.",
                    HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(),
                    new { tenantCode, login = loginOrCpf }, ct);
                _logger.LogWarning("Falha de login: usuário não encontrado. Tenant={TenantCode} Login={Login}", tenantCode, loginOrCpf);
                ModelState.AddModelError("", "Credenciais inválidas.");
                return View(vm);
            }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var correlationId = HttpContext.TraceIdentifier;

        if (!user.IsActive)
        {
            await _repo.RegisterLoginFailureAsync(user.TenantId, user.UserId, "Usuário inativo.", ip, userAgent, correlationId, ct);
            _logger.LogWarning("Falha de login: usuário inativo. Tenant={TenantId} UserId={UserId}", user.TenantId, user.UserId);
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
            await _repo.RegisterLoginFailureAsync(user.TenantId, user.UserId, "Usuário sem hash de senha.", ip, userAgent, correlationId, ct);
            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        var verify = PasswordVerificationResult.Failed;
        try
        {
            verify = _hasher.VerifyHashedPassword(new ApplicationUser
            {
                Id = user.UserId,
                TenantId = user.TenantId,
                Email = user.Email
            }, user.PasswordHash, vm.Password ?? string.Empty);
        }
        catch (FormatException)
        {
            await _repo.RegisterLoginFailureAsync(user.TenantId, user.UserId, "Hash de senha inválido no banco.", ip, userAgent, correlationId, ct);
            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        if (verify == PasswordVerificationResult.Failed)
        {
            await _repo.RegisterLoginFailureAsync(user.TenantId, user.UserId, "Senha inválida.", ip, userAgent, correlationId, ct);
            ModelState.AddModelError("", "Credenciais inválidas.");
            return View(vm);
        }

        await _repo.RegisterLoginSuccessAsync(user.TenantId, user.UserId, ip, userAgent, correlationId, ct);

        var rolesFromDatabase = await _repo.GetRolesAsync(user.TenantId, user.UserId, ct);

        var normalizedRoles = rolesFromDatabase
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(AppRoles.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedRoles.Count == 0)
            normalizedRoles.Add(AppRoles.Operador);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Name ?? "Usuário"),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("tenant_id", user.TenantId.ToString()),
            new("tenant_code", tenantCode),
            new("security_level", user.SecurityLevel ?? "PUBLIC"),
            new("mfa_enabled", user.MfaEnabled.ToString()),
            new("certificate_required", user.CertificateRequired.ToString()),
            new("can_sign_with_icp", user.CanSignWithIcp.ToString())
        };

        if (user.ServidorId.HasValue)
            claims.Add(new Claim("servidor_id", user.ServidorId.Value.ToString()));

        var sector = await ResolveUserSectorAsync(user.TenantId, user.UserId, ct);
        if (!string.IsNullOrWhiteSpace(sector))
        {
            claims.Add(new Claim("sector", sector));
            claims.Add(new Claim("setor", sector));
        }

        foreach (var role in normalizedRoles)
            claims.Add(new Claim(ClaimTypes.Role, role));

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

        if (user.MustChangePassword)
            return RedirectToAction(nameof(ChangePassword), "Account");

        // Regra centralizada de redirecionamento pós-login por perfil:
        // - ADMIN/ADMINISTRADOR respeitam ReturnUrl local seguro ou iniciam no GED.
        // - Ophir/Hospital respeitam apenas ReturnUrls permitidas ou iniciam em HospitalDocuments.
        var redirectResult = ResolvePostLoginRedirect(vm.ReturnUrl, user.UserName, normalizedRoles, principal);

        _logger.LogInformation(
            "Login redirect resolvido. User={UserId} Roles={Roles} IsFullAdmin={IsFullAdmin} Target={Target} Reason={Reason} ReturnUrl={ReturnUrl}",
            user.UserId,
            string.Join(",", normalizedRoles),
            RolePolicyHelper.IsFullAdmin(principal),
            redirectResult.TargetDescription,
            redirectResult.Reason,
            vm.ReturnUrl);

        await _audit.WriteAsync(
            tenantId: user.TenantId,
            userId: user.UserId,
            action: "HTTP",
            entityName: "login_redirect",
            entityId: null,
            summary: $"Login OK: redirect={redirectResult.TargetDescription}",
            ipAddress: ip,
            userAgent: userAgent,
            data: new
            {
                username = user.UserName,
                roles = normalizedRoles,
                returnUrl = vm.ReturnUrl,
                redirect = redirectResult.TargetDescription,
                redirectReason = redirectResult.Reason
            },
            ct: ct);

            return redirectResult.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro de autenticação no tenant {TenantCode}.", vm.TenantSlug);
            ModelState.AddModelError("", "Erro ao processar login.");
            return View(vm);
        }
    }

    private async Task<string?> ResolveUserSectorAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        try
        {
            return await _repo.GetUserSectorAsync(tenantId, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível carregar setor do usuário para indicador de perfil. Tenant={TenantId} UserId={UserId}", tenantId, userId);
            return null;
        }
    }

    private (IActionResult Result, string TargetDescription, string Reason) ResolvePostLoginRedirect(string? returnUrl, string? username, IReadOnlyCollection<string> normalizedRoles, ClaimsPrincipal principal)
    {
        var normalizedUsername = (username ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedReturnUrl = (returnUrl ?? string.Empty).Trim();

        var isAdmin = RolePolicyHelper.IsFullAdmin(principal) || normalizedRoles.Any(r => IsRole(r, AppRoles.Admin) || IsRole(r, AppRoles.Administrador));
        var isAdministradorOphir = _accessPolicy.IsAdministradorOphir(principal) || normalizedRoles.Any(r => IsRole(r, AppRoles.AdministradorOphir)) || IsRole(normalizedUsername, AppRoles.AdministradorOphir);
        var isArquivistaOphir = _accessPolicy.IsArquivistaOphir(principal) || normalizedRoles.Any(r => IsRole(r, AppRoles.ArquivistaOphir)) || IsRole(normalizedUsername, AppRoles.ArquivistaOphir);
        var isHospitalUser = AppMenuPolicy.IsHospitalUser(principal) || normalizedRoles.Any(r => IsRole(r, AppRoles.Hospital)) || IsRole(normalizedUsername, AppRoles.Hospital);

        if (isAdmin)
        {
            if (IsSafeLocalReturnUrl(normalizedReturnUrl) && AppStartRouteResolver.IsAllowedReturnUrlForUser(principal, normalizedReturnUrl))
                return (Redirect(normalizedReturnUrl), normalizedReturnUrl, "admin_return_url");

            return (Redirect(AppStartRouteResolver.AdminHome), AppStartRouteResolver.AdminHome, "admin_default_ged");
        }

        if (isAdministradorOphir)
        {
            if (IsSafeLocalReturnUrl(normalizedReturnUrl) && AppStartRouteResolver.IsAllowedReturnUrlForUser(principal, normalizedReturnUrl))
                return (Redirect(normalizedReturnUrl), normalizedReturnUrl, "administrador_ophir_allowed_return_url");

            return (Redirect(AppStartRouteResolver.HospitalHome), AppStartRouteResolver.HospitalHome, "administrador_ophir_default_hospital_documents");
        }

        if (isArquivistaOphir)
        {
            if (IsSafeLocalReturnUrl(normalizedReturnUrl) && AppStartRouteResolver.IsAllowedReturnUrlForUser(principal, normalizedReturnUrl))
                return (Redirect(normalizedReturnUrl), normalizedReturnUrl, "arquivista_ophir_allowed_return_url");

            return (Redirect(AppStartRouteResolver.HospitalHome), AppStartRouteResolver.HospitalHome, "arquivista_ophir_default_hospital_documents");
        }

        if (isHospitalUser)
        {
            if (IsSafeLocalReturnUrl(normalizedReturnUrl) && AppStartRouteResolver.IsAllowedReturnUrlForUser(principal, normalizedReturnUrl))
                return (Redirect(normalizedReturnUrl), normalizedReturnUrl, "hospital_allowed_return_url");

            return (Redirect(AppStartRouteResolver.HospitalHome), AppStartRouteResolver.HospitalHome, "hospital_default");
        }

        return (Redirect(AppStartRouteResolver.HospitalHome), AppStartRouteResolver.HospitalHome, "profile_default_hospital_documents");
    }

    private bool IsSafeLocalReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl);

    private static bool IsRole(string? value, string role)
        => string.Equals(NormalizeRole(value), NormalizeRole(role), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRole(string? value)
        => (value ?? string.Empty).Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        ViewData["Title"] = "Troca obrigatória de senha";
        return View();
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword, CancellationToken ct)
    {
        ViewData["Title"] = "Troca obrigatória de senha";

        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(tenantIdClaim, out var tenantId) || !Guid.TryParse(userIdClaim, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        if (string.IsNullOrWhiteSpace(currentPassword))
            ModelState.AddModelError(nameof(currentPassword), "Informe a senha atual.");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            ModelState.AddModelError(nameof(newPassword), "A nova senha deve possuir pelo menos 8 caracteres.");

        if (newPassword != confirmPassword)
            ModelState.AddModelError(nameof(confirmPassword), "A confirmação da nova senha não confere.");

        if (currentPassword == newPassword)
            ModelState.AddModelError(nameof(newPassword), "A nova senha deve ser diferente da senha atual.");

        if (!ModelState.IsValid)
            return View();

        var currentHash = await _repo.GetPasswordHashAsync(tenantId, userId, ct);

        if (string.IsNullOrWhiteSpace(currentHash) ||
            _hasher.VerifyHashedPassword(new ApplicationUser { Id = userId, TenantId = tenantId }, currentHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(currentPassword), "Senha atual inválida.");
            return View();
        }

        var newHash = _hasher.HashPassword(new ApplicationUser { Id = userId, TenantId = tenantId }, newPassword);

        await _repo.ResetPasswordByUserIdAsync(tenantId, userId, newHash, ct);

        var passwordChangeTarget = AppStartRouteResolver.GetDefaultHome(User);

        _logger.LogInformation(
            "Senha alterada com sucesso. Tenant={TenantId} UserId={UserId} Redirect={Redirect}",
            tenantId,
            userId,
            passwordChangeTarget);

        await _audit.WriteAsync(
            tenantId: tenantId,
            userId: userId,
            action: "SECURITY",
            entityName: "auth",
            entityId: userId,
            summary: "Usuário alterou a senha com sucesso.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: Request.Headers.UserAgent.ToString(),
            data: new
            {
                eventCode = "PASSWORD_CHANGED",
                redirect = passwordChangeTarget,
                correlationId = HttpContext.TraceIdentifier
            },
            ct: ct);

        TempData["Success"] = "Senha alterada com sucesso.";
        return Redirect(passwordChangeTarget);
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
        vm.TenantSlug = string.IsNullOrWhiteSpace(vm.TenantSlug) ? "default" : vm.TenantSlug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(vm.Cpf))
        {
            ModelState.AddModelError(nameof(vm.Cpf), "Informe o CPF.");
            return View("ForgotPassword", vm);
        }

        var usuario = await _repo.FindUserForPasswordRecoveryByCpfAsync(vm.TenantSlug, vm.Cpf, ct);

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
        vm.TenantSlug = string.IsNullOrWhiteSpace(vm.TenantSlug) ? "default" : vm.TenantSlug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(vm.Cpf))
        {
            ModelState.AddModelError(nameof(vm.Cpf), "Informe o CPF.");
            return View("ForgotPassword", vm);
        }

        var usuario = await _repo.FindUserForPasswordRecoveryByCpfAsync(vm.TenantSlug, vm.Cpf, ct);

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

        var newHash = _hasher.HashPassword(null!, vm.NovaSenha);

        await _repo.ResetPasswordByUserIdAsync(usuario.TenantId, usuario.UserId, newHash, ct);

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
