using System.Security.Claims;
using InovaGed.Application.Auth;
using InovaGed.Web.Models.Commercial;
using InovaGed.Web.Services.Commercial;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InovaGed.Web.Controllers;

[AllowAnonymous]
[Route("")]
public sealed class PublicCommercialController(IPublicCommercialService commercial) : Controller
{
    private static readonly PasswordHasher<ApplicationUser> Hasher = new();
    [HttpGet("")] public IActionResult Index() => View();
    [HttpGet("sobre")] public IActionResult About() => View();
    [HttpGet("metodologia")] public IActionResult Methodology() => View();
    [HttpGet("termos")] public IActionResult Terms() => View();
    [HttpGet("privacidade")] public IActionResult Privacy() => View();
    [HttpGet("planos")] public async Task<IActionResult> Plans(CancellationToken ct) => View(await commercial.GetPlansAsync(ct));
    [HttpGet("planos/comparar")] public async Task<IActionResult> Compare(CancellationToken ct) => View(await commercial.GetPlansAsync(ct));
    [HttpGet("demonstracao")] public IActionResult Demo() => View(new DemoRequestViewModel());

    [HttpPost("demonstracao"), ValidateAntiForgeryToken, EnableRateLimiting("commercial-write")]
    public async Task<IActionResult> Demo(DemoRequestViewModel model, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(model.Website)) return View("DemoThanks");
        if (!ModelState.IsValid) return View(model);
        await commercial.CreateLeadAsync(model, Ip(), ct);
        return View("DemoThanks");
    }

    [HttpGet("cadastro")] [HttpGet("teste-gratis")]
    public IActionResult Signup(string? plan = null) => View(new SignupViewModel { PlanCode = plan?.ToUpperInvariant() ?? "START" });

    [HttpPost("cadastro"), ValidateAntiForgeryToken, EnableRateLimiting("commercial-write")]
    public async Task<IActionResult> Signup(SignupViewModel model, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(model.Website)) return BadRequest();
        if (!StrongPassword(model.Password)) ModelState.AddModelError(nameof(model.Password), "Use 12 caracteres, com maiúscula, minúscula, número e símbolo.");
        var plans = await commercial.GetPlansAsync(ct);
        if (!plans.Any(p => p.IsActive && p.Code.Equals(model.PlanCode, StringComparison.OrdinalIgnoreCase))) ModelState.AddModelError(nameof(model.PlanCode), "Plano indisponível.");
        if (!ModelState.IsValid) return View(model);
        try
        {
            var hash = Hasher.HashPassword(new ApplicationUser { Email = model.Email }, model.Password);
            var result = await commercial.SignupAsync(model, hash, Ip(), ct);
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier,result.UserId.ToString()),new Claim(ClaimTypes.Name,model.Name),new Claim(ClaimTypes.Email,model.Email),new Claim(ClaimTypes.Role,"organization_admin"),new Claim("tenant_id",result.OrganizationId.ToString()),new Claim("tenant_code",result.TenantCode) };
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)),new AuthenticationProperties{IsPersistent=true});
            TempData["CommercialMessage"] = $"Conta criada. Seu teste vai até {result.TrialEndsAt:dd/MM/yyyy}.";
            return RedirectToAction(nameof(Onboarding));
        }
        catch (InvalidOperationException ex) when (ex.Message is "EMAIL_ALREADY_REGISTERED" or "ORGANIZATION_ALREADY_REGISTERED")
        { ModelState.AddModelError("", ex.Message == "EMAIL_ALREADY_REGISTERED" ? "Este e-mail já possui conta. Entre ou recupere sua senha." : "Esta organização já possui cadastro. Fale com o administrador da conta."); return View(model); }
    }

    [HttpGet("recuperar-senha")] public IActionResult ForgotPassword()=>View(new PasswordRequestViewModel());
    [HttpPost("recuperar-senha"),ValidateAntiForgeryToken,EnableRateLimiting("commercial-write")]
    public async Task<IActionResult> ForgotPassword(PasswordRequestViewModel model,CancellationToken ct) { if(ModelState.IsValid) await commercial.CreatePasswordResetAsync(model.Email,Ip(),ct); ViewBag.Sent=true; return View(model); }
    [HttpGet("redefinir-senha")] public IActionResult ResetPassword(string token)=>View(new PasswordResetViewModel{Token=token});
    [HttpPost("redefinir-senha"),ValidateAntiForgeryToken,EnableRateLimiting("commercial-write")]
    public async Task<IActionResult> ResetPassword(PasswordResetViewModel model,CancellationToken ct) { if(!StrongPassword(model.Password))ModelState.AddModelError(nameof(model.Password),"A senha não atende aos critérios de segurança."); if(!ModelState.IsValid)return View(model); var hash=Hasher.HashPassword(new ApplicationUser(),model.Password); if(!await commercial.ResetPasswordAsync(model.Token,hash,ct)){ModelState.AddModelError("","Link inválido ou expirado.");return View(model);} return RedirectToAction("Login","Account",new{message="Senha alterada com segurança."}); }

    [Authorize, HttpGet("onboarding")] public IActionResult Onboarding()=>View();
    [HttpGet("certificados/validar")] public IActionResult ValidateCertificate()=>View();
    [HttpGet("resultados/{token}")] public IActionResult SharedResult(string token) => string.IsNullOrWhiteSpace(token) ? NotFound() : View(model: token);
    private string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();
    internal static bool StrongPassword(string? value)=>value?.Length>=12&&value.Any(char.IsUpper)&&value.Any(char.IsLower)&&value.Any(char.IsDigit)&&value.Any(c=>!char.IsLetterOrDigit(c));
}
