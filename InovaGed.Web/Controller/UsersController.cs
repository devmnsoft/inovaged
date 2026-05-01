using InovaGed.Application.Identity;
using InovaGed.Application.Users;
using InovaGed.Infrastructure.Security;
using InovaGed.Web.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Users")]
public sealed class UsersController : Controller
{
    private readonly ILogger<UsersController> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAdminRepository _repo;
    private readonly IUserAdminQueries _queries;

    public UsersController(
        ILogger<UsersController> logger,
        ICurrentUser currentUser,
        IUserAdminRepository repo,
        IUserAdminQueries queries)
    {
        _logger = logger;
        _currentUser = currentUser;
        _repo = repo;
        _queries = queries;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(
        string? q,
        bool? active,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var tenantId = _currentUser.TenantId;

        var res = await _queries.ListUsersAsync(
            tenantId,
            q,
            active,
            page,
            pageSize,
            ct);

        var vm = new UserListVM
        {
            Q = q,
            Active = active,
            Page = page <= 0 ? 1 : page,
            PageSize = pageSize,
            Total = res.Total,
            Items = res.Items.Select(x => new UserListVM.Row
            {
                Id = x.Id,
                ServidorId = x.ServidorId,
                Name = x.Name,
                Cpf = x.Cpf,
                Matricula = x.Matricula,
                Cargo = x.Cargo,
                Funcao = x.Funcao,
                Setor = x.Setor,
                Lotacao = x.Lotacao,
                Unidade = x.Unidade,
                Email = x.Email,
                IsActive = x.IsActive,
                IsLocked = x.IsLocked,
                MustChangePassword = x.MustChangePassword,
                MfaEnabled = x.MfaEnabled,
                CertificateRequired = x.CertificateRequired,
                CanSignWithIcp = x.CanSignWithIcp,
                SecurityLevel = x.SecurityLevel,
                LastLoginAt = x.LastLoginAt,
                CreatedAt = x.CreatedAt,
                Roles = (x.RolesCsv ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList()
            }).ToList()
        };

        ViewData["Title"] = "Usuários e Servidores";
        ViewData["Subtitle"] = "Gerencie servidores, usuários, perfis, sigilo e credenciais";

        return View(vm);
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var vm = await BuildCreateVmAsync(new CreateUserVM(), ct);

        ViewData["Title"] = "Novo Servidor / Usuário";
        ViewData["Subtitle"] = "Cadastro institucional completo e criação de acesso ao sistema";

        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateUserVM vm, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var tenantId = _currentUser.TenantId;

        NormalizeCreateVm(vm);
        ValidateCreateVm(vm);

        if (!ModelState.IsValid)
        {
            await ReloadRolesAsync(vm, ct);
            return View(vm);
        }

        try
        {
            if (await _repo.CpfExistsAsync(tenantId, vm.Cpf, vm.ServidorId, ct))
            {
                ModelState.AddModelError(nameof(vm.Cpf), "Já existe servidor ativo cadastrado com este CPF.");
                await ReloadRolesAsync(vm, ct);
                return View(vm);
            }

            if (vm.CriarUsuarioAcesso &&
                await _repo.EmailExistsAsync(tenantId, vm.EmailLogin, null, ct))
            {
                ModelState.AddModelError(nameof(vm.EmailLogin), "Já existe usuário com este e-mail de login.");
                await ReloadRolesAsync(vm, ct);
                return View(vm);
            }

            var passwordHash = string.Empty;

            if (vm.CriarUsuarioAcesso)
            {
                var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
                passwordHash = hasher.HashPassword(null!, vm.Password);
            }

            var command = new CreateServidorUsuarioCommand
            {
                ServidorId = vm.ServidorId,

                NomeCompleto = vm.NomeCompleto,
                Cpf = vm.Cpf,
                Rg = vm.Rg,
                DataNascimento = vm.DataNascimento,

                EmailInstitucional = vm.EmailInstitucional,
                EmailAlternativo = vm.EmailAlternativo,
                Telefone = vm.Telefone,
                Celular = vm.Celular,

                Matricula = vm.Matricula,
                Cargo = vm.Cargo,
                Funcao = vm.Funcao,
                Setor = vm.Setor,
                Lotacao = vm.Lotacao,
                Unidade = vm.Unidade,
                TipoVinculo = vm.TipoVinculo,

                ConselhoProfissional = vm.ConselhoProfissional,
                NumeroConselho = vm.NumeroConselho,
                UfConselho = vm.UfConselho,
                Especialidade = vm.Especialidade,

                DataAdmissao = vm.DataAdmissao,
                SituacaoFuncional = vm.SituacaoFuncional,
                Observacao = vm.Observacao,

                CriarUsuarioAcesso = vm.CriarUsuarioAcesso,
                EmailLogin = vm.EmailLogin,
                UserName = vm.UserName,
                PasswordHash = passwordHash,
                IsActive = vm.IsActive,
                MustChangePassword = vm.MustChangePassword,
                MfaEnabled = vm.MfaEnabled,
                CertificateRequired = vm.CertificateRequired,
                CanSignWithIcp = vm.CanSignWithIcp,
                SecurityLevel = vm.SecurityLevel,

                RoleIds = vm.SelectedRoleIds
                    .Where(x => x != Guid.Empty)
                    .Distinct()
                    .ToList(),

                CreatedBy = _currentUser.UserId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                CorrelationId = HttpContext.TraceIdentifier
            };

            var result = await _repo.CreateServidorUsuarioAsync(
                tenantId,
                command,
                ct);

            TempData["Success"] = result.UserId.HasValue
                ? "Servidor cadastrado e usuário de acesso criado com sucesso."
                : "Servidor cadastrado com sucesso.";

            return RedirectToAction(nameof(Index));
        }
        catch (PostgresException pex) when (pex.SqlState == "23505")
        {
            _logger.LogWarning(
                pex,
                "Duplicidade ao criar servidor/usuário | Tenant={TenantId}",
                tenantId);

            ModelState.AddModelError("", "Já existe cadastro com o mesmo CPF, matrícula ou e-mail.");
            await ReloadRolesAsync(vm, ct);
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar servidor/usuário.");

            TempData["Error"] = "Erro ao criar servidor/usuário. Verifique os logs.";
            await ReloadRolesAsync(vm, ct);
            return View(vm);
        }
    }

    [HttpPost("SetActive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid id, bool active, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        await _repo.SetActiveAsync(
            _currentUser.TenantId,
            id,
            active,
            _currentUser.UserId,
            ct);

        TempData["Success"] = active ? "Usuário ativado com sucesso." : "Usuário inativado com sucesso.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("ResetPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        string newPassword,
        string confirmPassword,
        bool mustChangePassword = true,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            TempData["Error"] = "A nova senha deve possuir pelo menos 8 caracteres.";
            return RedirectToAction(nameof(Index));
        }

        if (newPassword != confirmPassword)
        {
            TempData["Error"] = "A confirmação da senha não confere.";
            return RedirectToAction(nameof(Index));
        }

        var hash = Pbkdf2PasswordHasher.Hash(newPassword);

        await _repo.ResetPasswordAsync(
            _currentUser.TenantId,
            id,
            hash,
            mustChangePassword,
            _currentUser.UserId,
            ct);

        TempData["Success"] = "Senha redefinida com sucesso.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<CreateUserVM> BuildCreateVmAsync(CreateUserVM vm, CancellationToken ct)
    {
        await ReloadRolesAsync(vm, ct);

        vm.IsActive = true;
        vm.MustChangePassword = true;
        vm.CriarUsuarioAcesso = true;
        vm.SecurityLevel = "PUBLIC";
        vm.SituacaoFuncional = "ATIVO";

        return vm;
    }

    private async Task ReloadRolesAsync(CreateUserVM vm, CancellationToken ct)
    {
        var roles = await _repo.ListRolesAsync(_currentUser.TenantId, ct);

        vm.AvailableRoles = roles
            .Select(r => new CreateUserVM.RoleItem
            {
                Id = r.Id,
                Name = r.Name
            })
            .ToList();
    }

    private void ValidateCreateVm(CreateUserVM vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Cpf) || OnlyDigits(vm.Cpf).Length != 11)
            ModelState.AddModelError(nameof(vm.Cpf), "CPF inválido. Informe 11 dígitos.");

        if (vm.CriarUsuarioAcesso)
        {
            if (string.IsNullOrWhiteSpace(vm.EmailLogin))
                ModelState.AddModelError(nameof(vm.EmailLogin), "Informe o e-mail de login.");

            if (string.IsNullOrWhiteSpace(vm.Password))
                ModelState.AddModelError(nameof(vm.Password), "Informe a senha inicial.");

            if (vm.Password?.Length < 8)
                ModelState.AddModelError(nameof(vm.Password), "A senha deve possuir pelo menos 8 caracteres.");

            if (vm.Password != vm.ConfirmPassword)
                ModelState.AddModelError(nameof(vm.ConfirmPassword), "A confirmação não confere com a senha.");

            if (vm.SelectedRoleIds is null || vm.SelectedRoleIds.Count == 0)
                ModelState.AddModelError(nameof(vm.SelectedRoleIds), "Selecione ao menos um perfil de acesso.");
        }
    }

    private static void NormalizeCreateVm(CreateUserVM vm)
    {
        vm.NomeCompleto = Trim(vm.NomeCompleto);
        vm.Cpf = FormatCpf(vm.Cpf);
        vm.Rg = TrimOrNull(vm.Rg);
        vm.EmailInstitucional = TrimLowerOrNull(vm.EmailInstitucional);
        vm.EmailAlternativo = TrimLowerOrNull(vm.EmailAlternativo);
        vm.EmailLogin = TrimLowerOrNull(vm.EmailLogin) ?? "";
        vm.UserName = TrimOrNull(vm.UserName);
        vm.Telefone = TrimOrNull(vm.Telefone);
        vm.Celular = TrimOrNull(vm.Celular);
        vm.Matricula = TrimOrNull(vm.Matricula);
        vm.Cargo = TrimOrNull(vm.Cargo);
        vm.Funcao = TrimOrNull(vm.Funcao);
        vm.Setor = TrimOrNull(vm.Setor);
        vm.Lotacao = TrimOrNull(vm.Lotacao);
        vm.Unidade = TrimOrNull(vm.Unidade);
        vm.TipoVinculo = TrimOrNull(vm.TipoVinculo);
        vm.ConselhoProfissional = TrimOrNull(vm.ConselhoProfissional);
        vm.NumeroConselho = TrimOrNull(vm.NumeroConselho);
        vm.UfConselho = TrimOrNull(vm.UfConselho)?.ToUpperInvariant();
        vm.Especialidade = TrimOrNull(vm.Especialidade);
        vm.SituacaoFuncional = string.IsNullOrWhiteSpace(vm.SituacaoFuncional)
            ? "ATIVO"
            : vm.SituacaoFuncional.Trim().ToUpperInvariant();

        vm.SecurityLevel = string.IsNullOrWhiteSpace(vm.SecurityLevel)
            ? "PUBLIC"
            : vm.SecurityLevel.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(vm.EmailLogin))
        {
            vm.EmailLogin = vm.EmailInstitucional
                ?? vm.EmailAlternativo
                ?? "";
        }

        if (string.IsNullOrWhiteSpace(vm.UserName))
        {
            vm.UserName = vm.EmailLogin;
        }
    }

    private static string Trim(string? value)
    {
        return (value ?? "").Trim();
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TrimLowerOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private static string OnlyDigits(string? value)
    {
        return new string((value ?? "").Where(char.IsDigit).ToArray());
    }

    private static string FormatCpf(string? value)
    {
        var digits = OnlyDigits(value);

        if (digits.Length != 11)
            return value?.Trim() ?? "";

        return Convert.ToUInt64(digits).ToString(@"000\.000\.000\-00");
    }
}