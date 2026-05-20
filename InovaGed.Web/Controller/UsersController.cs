using InovaGed.Application.Common.Security;
using InovaGed.Application.Identity;
using InovaGed.Application.Parameters;
using InovaGed.Application.Users;
using InovaGed.Web.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Users")]
public sealed class UsersController : Controller
{
    private const string RoleArquivistaOphir = "ArquivistaOphir";
    private const string RoleAdministradorOphir = "AdministradorOphir";

    private static readonly string[] UserParameterCategories =
    {
        "CARGO", "FUNCAO", "SETOR", "LOTACAO", "UNIDADE", "TIPO_VINCULO",
        "SITUACAO_FUNCIONAL", "CONSELHO_PROFISSIONAL", "ESPECIALIDADE", "NIVEL_SIGILO"
    };

    private readonly ILogger<UsersController> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAdminRepository _repo;
    private readonly IUserAdminQueries _queries;
    private readonly IParameterRepository _parameters;

    public UsersController(
        ILogger<UsersController> logger,
        ICurrentUser currentUser,
        IUserAdminRepository repo,
        IUserAdminQueries queries,
        IParameterRepository parameters)
    {
        _logger = logger;
        _currentUser = currentUser;
        _repo = repo;
        _queries = queries;
        _parameters = parameters;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(string? q, bool? active, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanManageUsers()) return RedirectToAction("AccessDenied", "Account");

        var tenantId = _currentUser.TenantId;
        await EnsureOphirSeedBaselineAsync(tenantId, ct);

        var res = await _queries.ListUsersAsync(tenantId, q, active, page, pageSize, ct);
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
                Name = x.Name,
                Email = x.Email,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                Roles = (x.RolesCsv ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList()
            }).ToList()
        };

        SetDynamicMenu();
        ViewData["Title"] = "Usuários e Servidores";
        return View(vm);
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanManageUsers()) return RedirectToAction("AccessDenied", "Account");

        var vm = await BuildCreateVmAsync(new CreateUserVM(), ct);
        SetDynamicMenu();
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateUserVM vm, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanManageUsers()) return RedirectToAction("AccessDenied", "Account");

        var tenantId = _currentUser.TenantId;
        NormalizeCreateVm(vm);
        ValidateCreateVm(vm);

        if (!ModelState.IsValid)
        {
            await ReloadCreateLookupsAsync(vm, ct);
            SetDynamicMenu();
            return View(vm);
        }

        try
        {
            if (await _repo.CpfExistsAsync(tenantId, vm.Cpf, vm.ServidorId, ct))
                ModelState.AddModelError(nameof(vm.Cpf), "Já existe servidor ativo cadastrado com este CPF.");

            if (vm.CriarUsuarioAcesso && await _repo.EmailExistsAsync(tenantId, vm.EmailLogin, null, ct))
                ModelState.AddModelError(nameof(vm.EmailLogin), "Já existe usuário com este e-mail de login.");

            if (!ModelState.IsValid)
            {
                await ReloadCreateLookupsAsync(vm, ct);
                SetDynamicMenu();
                return View(vm);
            }

            var passwordHash = string.Empty;
            if (vm.CriarUsuarioAcesso)
            {
                var hasher = new PasswordHasher<ApplicationUser>();
                passwordHash = hasher.HashPassword(new ApplicationUser { Id = Guid.NewGuid(), TenantId = tenantId, Email = vm.EmailLogin }, vm.Password);
            }

            await _repo.CreateServidorUsuarioAsync(tenantId, new CreateServidorUsuarioCommand
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
                RoleIds = vm.SelectedRoleIds.Where(x => x != Guid.Empty).Distinct().ToList(),
                CreatedBy = _currentUser.UserId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                CorrelationId = HttpContext.TraceIdentifier
            }, ct);

            TempData["Success"] = "Servidor/usuário criado com sucesso.";
            return RedirectToAction(nameof(Index));
        }
        catch (PostgresException pex) when (pex.SqlState == "23505")
        {
            _logger.LogWarning(pex, "Duplicidade ao criar usuário");
            ModelState.AddModelError(string.Empty, "Já existe cadastro com o mesmo CPF, matrícula ou e-mail.");
            await ReloadCreateLookupsAsync(vm, ct);
            SetDynamicMenu();
            return View(vm);
        }
    }

    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanManageUsers()) return RedirectToAction("AccessDenied", "Account");

        var dto = await _repo.GetForEditAsync(_currentUser.TenantId, id, ct);
        if (dto is null) return RedirectToAction("AccessDenied", "Account");

        var vm = new EditUserVM
        {
            UserId = dto.UserId, ServidorId = dto.ServidorId, NomeCompleto = dto.NomeCompleto, Cpf = dto.Cpf, Rg = dto.Rg,
            DataNascimento = dto.DataNascimento, EmailInstitucional = dto.EmailInstitucional, EmailAlternativo = dto.EmailAlternativo,
            Telefone = dto.Telefone, Celular = dto.Celular, Matricula = dto.Matricula, Cargo = dto.Cargo, Funcao = dto.Funcao,
            Setor = dto.Setor, Lotacao = dto.Lotacao, Unidade = dto.Unidade, TipoVinculo = dto.TipoVinculo,
            ConselhoProfissional = dto.ConselhoProfissional, NumeroConselho = dto.NumeroConselho, UfConselho = dto.UfConselho,
            Especialidade = dto.Especialidade, DataAdmissao = dto.DataAdmissao, SituacaoFuncional = dto.SituacaoFuncional,
            Observacao = dto.Observacao, EmailLogin = dto.EmailLogin, UserName = dto.UserName, IsActive = dto.IsActive,
            MustChangePassword = dto.MustChangePassword, MfaEnabled = dto.MfaEnabled, CertificateRequired = dto.CertificateRequired,
            CanSignWithIcp = dto.CanSignWithIcp, SecurityLevel = dto.SecurityLevel, SelectedRoleIds = dto.RoleIds
        };

        await ReloadEditLookupsAsync(vm, ct);
        SetDynamicMenu();
        return View(vm);
    }

    [HttpPost("Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, [FromForm] EditUserVM vm, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanManageUsers()) return RedirectToAction("AccessDenied", "Account");
        if (id != vm.UserId) return RedirectToAction("AccessDenied", "Account");

        NormalizeEditVm(vm);
        ValidateEditVm(vm);

        if (!ModelState.IsValid)
        {
            await ReloadEditLookupsAsync(vm, ct);
            SetDynamicMenu();
            return View(vm);
        }

        var tenantId = _currentUser.TenantId;
        if (await _repo.CpfExistsAsync(tenantId, vm.Cpf, vm.ServidorId, ct))
            ModelState.AddModelError(nameof(vm.Cpf), "Já existe outro servidor ativo cadastrado com este CPF.");

        if (await _repo.EmailExistsAsync(tenantId, vm.EmailLogin, vm.UserId, ct))
            ModelState.AddModelError(nameof(vm.EmailLogin), "Já existe outro usuário com este e-mail de login.");

        if (!ModelState.IsValid)
        {
            await ReloadEditLookupsAsync(vm, ct);
            SetDynamicMenu();
            return View(vm);
        }

        await _repo.UpdateServidorUsuarioAsync(tenantId, new UpdateServidorUsuarioCommand
        {
            UserId = vm.UserId, ServidorId = vm.ServidorId, NomeCompleto = vm.NomeCompleto, Cpf = vm.Cpf, Rg = vm.Rg,
            DataNascimento = vm.DataNascimento, EmailInstitucional = vm.EmailInstitucional, EmailAlternativo = vm.EmailAlternativo,
            Telefone = vm.Telefone, Celular = vm.Celular, Matricula = vm.Matricula, Cargo = vm.Cargo, Funcao = vm.Funcao,
            Setor = vm.Setor, Lotacao = vm.Lotacao, Unidade = vm.Unidade, TipoVinculo = vm.TipoVinculo,
            ConselhoProfissional = vm.ConselhoProfissional, NumeroConselho = vm.NumeroConselho, UfConselho = vm.UfConselho,
            Especialidade = vm.Especialidade, DataAdmissao = vm.DataAdmissao, SituacaoFuncional = vm.SituacaoFuncional,
            Observacao = vm.Observacao, EmailLogin = vm.EmailLogin, UserName = vm.UserName, IsActive = vm.IsActive,
            MustChangePassword = vm.MustChangePassword, MfaEnabled = vm.MfaEnabled, CertificateRequired = vm.CertificateRequired,
            CanSignWithIcp = vm.CanSignWithIcp, SecurityLevel = vm.SecurityLevel,
            RoleIds = vm.SelectedRoleIds.Where(x => x != Guid.Empty).Distinct().ToList(),
            UpdatedBy = _currentUser.UserId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            CorrelationId = HttpContext.TraceIdentifier
        }, ct);

        TempData["Success"] = "Cadastro atualizado com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("SetActive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid id, bool active, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanManageUsers()) return RedirectToAction("AccessDenied", "Account");
        await _repo.SetActiveAsync(_currentUser.TenantId, id, active, _currentUser.UserId, ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("ResetPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(Guid id, string newPassword, string confirmPassword, bool mustChangePassword = true, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanManageUsers()) return RedirectToAction("AccessDenied", "Account");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8 || newPassword != confirmPassword)
        {
            TempData["Error"] = "Senha inválida ou confirmação divergente.";
            return RedirectToAction(nameof(Index));
        }

        var hasher = new PasswordHasher<ApplicationUser>();
        var hash = hasher.HashPassword(new ApplicationUser { Id = id, TenantId = _currentUser.TenantId }, newPassword);
        await _repo.ResetPasswordAsync(_currentUser.TenantId, id, hash, mustChangePassword, _currentUser.UserId, ct);
        TempData["Success"] = "Senha redefinida com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<CreateUserVM> BuildCreateVmAsync(CreateUserVM vm, CancellationToken ct)
    {
        vm.IsActive = true;
        vm.MustChangePassword = true;
        vm.CriarUsuarioAcesso = true;
        vm.SecurityLevel = "PUBLIC";
        vm.SituacaoFuncional = "ATIVO";
        await ReloadCreateLookupsAsync(vm, ct);
        return vm;
    }

    private async Task ReloadCreateLookupsAsync(CreateUserVM vm, CancellationToken ct)
    {
        var roles = await _repo.ListRolesAsync(_currentUser.TenantId, ct);
        vm.AvailableRoles = roles.Select(r => new CreateUserVM.RoleItem { Id = r.Id, Name = r.Name }).ToList();
        var options = await _parameters.ListOptionsAsync(_currentUser.TenantId, UserParameterCategories, ct);
        FillCreateOptions(vm, options);
    }

    private async Task ReloadEditLookupsAsync(EditUserVM vm, CancellationToken ct)
    {
        var roles = await _repo.ListRolesAsync(_currentUser.TenantId, ct);
        vm.AvailableRoles = roles.Select(r => new EditUserVM.RoleItem { Id = r.Id, Name = r.Name }).ToList();
        var options = await _parameters.ListOptionsAsync(_currentUser.TenantId, UserParameterCategories, ct);
        FillEditOptions(vm, options);
    }

    private static void FillCreateOptions(CreateUserVM vm, IReadOnlyList<ParameterSelectOption> options)
    {
        vm.Cargos = MapCreate(options, "CARGO", vm.Cargo);
        vm.Funcoes = MapCreate(options, "FUNCAO", vm.Funcao);
        vm.Setores = MapCreate(options, "SETOR", vm.Setor);
        vm.Lotacoes = MapCreate(options, "LOTACAO", vm.Lotacao);
        vm.Unidades = MapCreate(options, "UNIDADE", vm.Unidade);
        vm.TiposVinculo = MapCreate(options, "TIPO_VINCULO", vm.TipoVinculo);
        vm.ConselhosProfissionais = MapCreate(options, "CONSELHO_PROFISSIONAL", vm.ConselhoProfissional);
        vm.Especialidades = MapCreate(options, "ESPECIALIDADE", vm.Especialidade);
        vm.SituacoesFuncionais = MapCreate(options, "SITUACAO_FUNCIONAL", vm.SituacaoFuncional);
        vm.SecurityLevels = MapCreate(options, "NIVEL_SIGILO", vm.SecurityLevel);
    }

    private static void FillEditOptions(EditUserVM vm, IReadOnlyList<ParameterSelectOption> options)
    {
        vm.Cargos = MapEdit(options, "CARGO", vm.Cargo);
        vm.Funcoes = MapEdit(options, "FUNCAO", vm.Funcao);
        vm.Setores = MapEdit(options, "SETOR", vm.Setor);
        vm.Lotacoes = MapEdit(options, "LOTACAO", vm.Lotacao);
        vm.Unidades = MapEdit(options, "UNIDADE", vm.Unidade);
        vm.TiposVinculo = MapEdit(options, "TIPO_VINCULO", vm.TipoVinculo);
        vm.ConselhosProfissionais = MapEdit(options, "CONSELHO_PROFISSIONAL", vm.ConselhoProfissional);
        vm.Especialidades = MapEdit(options, "ESPECIALIDADE", vm.Especialidade);
        vm.SituacoesFuncionais = MapEdit(options, "SITUACAO_FUNCIONAL", vm.SituacaoFuncional);
        vm.SecurityLevels = MapEdit(options, "NIVEL_SIGILO", vm.SecurityLevel);
    }

    private static List<CreateUserVM.SelectItem> MapCreate(IReadOnlyList<ParameterSelectOption> options, string code, string? current)
    {
        var list = options.Where(x => x.CategoryCode == code).Select(x => new CreateUserVM.SelectItem(x.Value, x.Text)).ToList();
        EnsureSelected(list, current);
        return list;
    }

    private static List<EditUserVM.SelectItem> MapEdit(IReadOnlyList<ParameterSelectOption> options, string code, string? current)
    {
        var list = options.Where(x => x.CategoryCode == code).Select(x => new EditUserVM.SelectItem(x.Value, x.Text)).ToList();
        EnsureSelected(list, current);
        return list;
    }

    private void ValidateCreateVm(CreateUserVM vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Cpf) || OnlyDigits(vm.Cpf).Length != 11)
            ModelState.AddModelError(nameof(vm.Cpf), "CPF inválido. Informe 11 dígitos.");
        if (vm.CriarUsuarioAcesso)
        {
            if (string.IsNullOrWhiteSpace(vm.EmailLogin)) ModelState.AddModelError(nameof(vm.EmailLogin), "Informe o e-mail de login.");
            if (string.IsNullOrWhiteSpace(vm.Password) || vm.Password.Length < 8) ModelState.AddModelError(nameof(vm.Password), "A senha deve possuir pelo menos 8 caracteres.");
            if (vm.Password != vm.ConfirmPassword) ModelState.AddModelError(nameof(vm.ConfirmPassword), "A confirmação não confere com a senha.");
            if (vm.SelectedRoleIds.Count == 0) ModelState.AddModelError(nameof(vm.SelectedRoleIds), "Selecione ao menos um perfil de acesso.");
        }
    }

    private void ValidateEditVm(EditUserVM vm)
    {
        if (vm.UserId == Guid.Empty) ModelState.AddModelError(nameof(vm.UserId), "Usuário inválido.");
        if (string.IsNullOrWhiteSpace(vm.Cpf) || OnlyDigits(vm.Cpf).Length != 11) ModelState.AddModelError(nameof(vm.Cpf), "CPF inválido. Informe 11 dígitos.");
        if (string.IsNullOrWhiteSpace(vm.EmailLogin)) ModelState.AddModelError(nameof(vm.EmailLogin), "Informe o e-mail de login.");
        if (vm.SelectedRoleIds.Count == 0) ModelState.AddModelError(nameof(vm.SelectedRoleIds), "Selecione ao menos um perfil de acesso.");
    }

    private static void NormalizeCreateVm(CreateUserVM vm)
    {
        vm.NomeCompleto = Trim(vm.NomeCompleto);
        vm.Cpf = FormatCpf(vm.Cpf);
        vm.EmailInstitucional = TrimLowerOrNull(vm.EmailInstitucional);
        vm.EmailAlternativo = TrimLowerOrNull(vm.EmailAlternativo);
        vm.EmailLogin = TrimLowerOrNull(vm.EmailLogin) ?? vm.EmailInstitucional ?? vm.EmailAlternativo ?? string.Empty;
        vm.UserName = TrimOrNull(vm.UserName) ?? vm.EmailLogin;
    }

    private static void NormalizeEditVm(EditUserVM vm)
    {
        vm.NomeCompleto = Trim(vm.NomeCompleto);
        vm.Cpf = FormatCpf(vm.Cpf);
        vm.EmailInstitucional = TrimLowerOrNull(vm.EmailInstitucional);
        vm.EmailAlternativo = TrimLowerOrNull(vm.EmailAlternativo);
        vm.EmailLogin = TrimLowerOrNull(vm.EmailLogin) ?? string.Empty;
        vm.UserName = TrimOrNull(vm.UserName) ?? vm.EmailLogin;
    }

    private static void EnsureSelected<T>(List<T> list, string? current) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(current)) return;
        var valueProp = typeof(T).GetProperty("Value");
        var textProp = typeof(T).GetProperty("Text");
        if (valueProp is null || textProp is null) return;
        if (list.Any(x => string.Equals(valueProp.GetValue(x)?.ToString(), current, StringComparison.OrdinalIgnoreCase))) return;
        var item = new T();
        valueProp.SetValue(item, current);
        textProp.SetValue(item, current + " (valor atual)");
        list.Insert(0, item);
    }

    private bool CanManageUsers() => User.IsInRole("Admin") || User.IsInRole(RoleAdministradorOphir);

    private void SetDynamicMenu()
    {
        // Menu dinâmico para renderização na View/Layout.
        ViewData["CanAccessHospitalDocuments"] = User.IsInRole(RoleArquivistaOphir) || User.IsInRole(RoleAdministradorOphir);
        ViewData["CanAccessLoans"] = User.IsInRole(RoleAdministradorOphir);
    }

    private async Task EnsureOphirSeedBaselineAsync(Guid tenantId, CancellationToken ct)
    {
        // Seed de baseline: valida existência das roles obrigatórias e sinaliza se faltarem.
        var roles = await _repo.ListRolesAsync(tenantId, ct);
        var names = roles.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(RoleArquivistaOphir) || !names.Contains(RoleAdministradorOphir))
            _logger.LogWarning("Roles Ophir ausentes. Execute o seed inicial para criar roles/usuários de exemplo.");
    }

    private static string Trim(string? value) => (value ?? string.Empty).Trim();
    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? TrimLowerOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    private static string OnlyDigits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string FormatCpf(string? value)
    {
        var digits = OnlyDigits(value);
        return digits.Length == 11 ? Convert.ToUInt64(digits).ToString(@"000\.000\.000\-00") : Trim(value);
    }
}
