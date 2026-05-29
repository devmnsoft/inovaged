using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Application.Parameters;
using InovaGed.Application.Users;
using InovaGed.Infrastructure.Security;
using InovaGed.Web.Models.Users;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Users")]
public sealed class UsersController : AppControllerBase
{
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
    private readonly UserService _userService;
    private readonly IAuditWriter _auditWriter;

    public UsersController(
        ILogger<UsersController> logger,
        ICurrentUser currentUser,
        IUserAdminRepository repo,
        IUserAdminQueries queries,
        IParameterRepository parameters,
        UserService userService,
        IAuditWriter auditWriter)
    {
        _logger = logger;
        _currentUser = currentUser;
        _repo = repo;
        _queries = queries;
        _parameters = parameters;
        _userService = userService;
        _auditWriter = auditWriter;
    }

    [HttpPost("Unlock/{id:guid}")]
    [Authorize(Roles = AppRoles.Admin + ",ADMINISTRATOR")]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var correlationId = HttpContext.TraceIdentifier;

        try
        {
            var unlocked = await _userService.UnlockUserAsync(
                id,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                ct);

            if (!unlocked)
                return JsonError("Usuário não encontrado.", "Validação", "UserNotFound", false, 404);

            _logger.LogInformation(
                "Usuário desbloqueado. TenantId={TenantId} UserId={UserId} TargetUserId={TargetUserId} Path={Path} Method={Method} CorrelationId={CorrelationId}",
                tenantId,
                userId,
                id,
                Request.Path,
                Request.Method,
                correlationId);

            return JsonSuccess("Usuário desbloqueado com sucesso.", new { userId = id });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Request cancelado pelo cliente em desbloqueio de usuário. TenantId={TenantId} UserId={UserId} TargetUserId={TargetUserId} Path={Path} CorrelationId={CorrelationId}",
                tenantId,
                userId,
                id,
                Request.Path,
                correlationId);

            return JsonError("Operação cancelada pelo cliente.", "Cancelamento", null, true, 499);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "Acesso negado ao desbloquear usuário. TenantId={TenantId} UserId={UserId} TargetUserId={TargetUserId} Path={Path} CorrelationId={CorrelationId}",
                tenantId,
                userId,
                id,
                Request.Path,
                correlationId);
            return JsonError("Você não possui permissão para executar esta ação.", "Autorização", "AccessDenied", false, 403);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao desbloquear usuário. TenantId={TenantId} UserId={UserId} TargetUserId={TargetUserId} Path={Path} Method={Method} CorrelationId={CorrelationId}",
                tenantId,
                userId,
                id,
                Request.Path,
                Request.Method,
                correlationId);

            return JsonError("Erro interno ao processar a solicitação. Informe o código de rastreio ao suporte.", "Servidor", "UnlockUserError", true, 500);
        }
    }

    [HttpGet("")]
    [HttpGet("Index")]
    [Authorize(Roles = AppRoles.Admin + ",ADMINISTRATOR")]
    public async Task<IActionResult> Index(string? q, bool? active, bool? locked, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var res = await _queries.ListUsersAsync(tenantId, q, active, locked, page, pageSize, ct);

        var vm = new UserListVM
        {
            Q = q,
            Active = active,
            Locked = locked,
            Page = page,
            PageSize = pageSize,
            Total = res.Total,
            Items = res.Items.Select(x =>
            {
                var effectiveId =
                    x.ServidorId.HasValue && x.ServidorId.Value != Guid.Empty
                        ? x.ServidorId.Value
                        : x.UserId.HasValue && x.UserId.Value != Guid.Empty
                            ? x.UserId.Value
                            : Guid.Empty;

                if (effectiveId == Guid.Empty)
                {
                    _logger.LogWarning(
                        "Linha de usuário sem identificador válido para edição. Tenant={TenantId} Name={Name} Email={Email} ServidorId={ServidorId} UserId={UserId}",
                        tenantId,
                        x.Name,
                        x.Email,
                        x.ServidorId,
                        x.UserId);
                }

                return new UserListVM.Row
                {
                    Id = effectiveId,
                    ServidorId = x.ServidorId,
                    UserId = x.UserId,
                    EditIdSource = x.EditIdSource,
                    Name = x.Name,
                    Email = x.Email,
                    Cpf = x.Cpf,
                    Matricula = x.Matricula,
                    Cargo = x.Cargo,
                    Funcao = x.Funcao,
                    Setor = x.Setor,
                    Lotacao = x.Lotacao,
                    Roles = (x.RolesCsv ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim())
                        .Where(r => !string.IsNullOrWhiteSpace(r))
                        .ToList(),
                    SecurityLevel = x.SecurityLevel,
                    IsActive = x.IsActive,
                    IsLocked = x.IsLocked,
                    MustChangePassword = x.MustChangePassword,
                    MfaEnabled = x.MfaEnabled,
                    CertificateRequired = x.CertificateRequired,
                    CanSignWithIcp = x.CanSignWithIcp,
                    LastLoginAt = x.LastLoginAt
                };
            }).ToList()
        };

        ViewData["Title"] = "Usuários e Servidores";
        ViewData["Subtitle"] = "Gerencie servidores, usuários, perfis, sigilo e credenciais";
        return View(vm);
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var vm = await BuildCreateVmAsync(new CreateUserVM(), ct);
        ViewData["Title"] = "Novo Servidor / Usuário";
        ViewData["Subtitle"] = "Cadastro institucional completo e criação de acesso ao sistema";
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateUserVM vm, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        NormalizeCreateVm(vm);
        ValidateCreateVm(vm);

        if (!ModelState.IsValid)
        {
            await ReloadCreateLookupsAsync(vm, ct);
            return View(vm);
        }

        try
        {
            if (await _repo.CpfExistsAsync(tenantId, vm.Cpf, vm.ServidorId, ct))
            {
                ModelState.AddModelError(nameof(vm.Cpf), "Já existe servidor ativo cadastrado com este CPF.");
                await ReloadCreateLookupsAsync(vm, ct);
                return View(vm);
            }

            if (vm.CriarUsuarioAcesso && await _repo.EmailExistsAsync(tenantId, vm.EmailLogin, null, ct))
            {
                ModelState.AddModelError(nameof(vm.EmailLogin), "Já existe usuário com este e-mail de login.");
                await ReloadCreateLookupsAsync(vm, ct);
                return View(vm);
            }

            var passwordHash = string.Empty;
            if (vm.CriarUsuarioAcesso)
            {
                var hasher = new PasswordHasher<object>();
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
                RoleIds = vm.CriarUsuarioAcesso ? vm.SelectedRoleIds.Where(x => x != Guid.Empty).Distinct().ToList() : new List<Guid>(),
                CreatedBy = _currentUser.UserId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                CorrelationId = HttpContext.TraceIdentifier
            };

            var result = await _repo.CreateServidorUsuarioAsync(tenantId, command, ct);
            TempData["Success"] = result.UserId.HasValue ? "Servidor cadastrado e usuário de acesso criado com sucesso." : "Servidor cadastrado com sucesso.";
            return RedirectToAction(nameof(Index));
        }
        catch (PostgresException pex) when (pex.SqlState == "23505")
        {
            _logger.LogWarning(pex, "Duplicidade ao criar servidor/usuário | Tenant={TenantId}", tenantId);
            ModelState.AddModelError("", "Já existe cadastro com o mesmo CPF, matrícula ou e-mail.");
            await ReloadCreateLookupsAsync(vm, ct);
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar servidor/usuário.");
            TempData["Error"] = "Erro ao criar servidor/usuário. Verifique os logs.";
            await ReloadCreateLookupsAsync(vm, ct);
            return View(vm);
        }
    }

    [HttpGet("Edit/{id:guid}")]
    [Authorize(Roles = AppRoles.Admin + ",ADMINISTRATOR")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        if (id == Guid.Empty)
        {
            _logger.LogWarning(
                "Tentativa de edição com Guid.Empty. Tenant={TenantId} User={UserId} CorrelationId={CorrelationId}",
                _currentUser.TenantId,
                _currentUser.UserId,
                HttpContext.TraceIdentifier);

            TempData["Error"] = "Cadastro sem identificador válido para edição. Verifique a consistência da listagem.";
            return RedirectToAction(nameof(Index));
        }

        var isAdmin =
            User.IsInRole(AppRoles.Admin) ||
            User.IsInRole("ADMIN") ||
            User.IsInRole("ADMINISTRATOR");

        _logger.LogInformation(
            "Edit user requested. Tenant={TenantId} CurrentUser={CurrentUserId} IsAdmin={IsAdmin} RouteId={RouteId} CorrelationId={CorrelationId}",
            tenantId,
            _currentUser.UserId,
            isAdmin,
            id,
            HttpContext.TraceIdentifier);

        var dto = await _repo.GetForEditByServidorIdAsync(tenantId, id, isAdmin, ct);
        if (dto is null && isAdmin)
        {
            _logger.LogWarning(
                "Edit by ServidorId failed for ADMIN. Trying fallback by UserId. Tenant={TenantId} RouteId={RouteId}",
                tenantId,
                id);
            dto = await _repo.GetForEditByUserIdAsync(tenantId, id, ct);
        }
        if (dto is null && isAdmin)
        {
            _logger.LogWarning(
                "Edit by UserId failed for ADMIN. Trying fallback by vw_user_admin_list. Tenant={TenantId} RouteId={RouteId}",
                tenantId,
                id);
            dto = await _repo.GetForEditFromAdminListAsync(tenantId, id, ct);
        }

        if (dto is null)
        {
            var diag = await _repo.DiagnoseUserEditIdAsync(tenantId, id, ct);
            _logger.LogWarning("Servidor/usuário não encontrado para edição. Tenant={TenantId} RouteId={RouteId} IsAdmin={IsAdmin} Diagnosis={Diagnosis} CorrelationId={CorrelationId}",
                tenantId,
                id,
                isAdmin,
                diag,
                HttpContext.TraceIdentifier);
            TempData["Error"] = isAdmin
                ? "Cadastro não encontrado para edição. Verifique o diagnóstico técnico nos logs."
                : "Você não possui permissão para editar este cadastro.";
            return RedirectToAction(nameof(Index));
        }

        var vm = new EditUserVM
        {
            UserId = dto.UserId,
            ServidorId = dto.ServidorId,
            PossuiUsuarioAcesso = dto.UserId.HasValue,
            CriarUsuarioAcesso = false,
            NomeCompleto = dto.NomeCompleto,
            Cpf = dto.Cpf,
            Rg = dto.Rg,
            DataNascimento = dto.DataNascimento,
            EmailInstitucional = dto.EmailInstitucional,
            EmailAlternativo = dto.EmailAlternativo,
            Telefone = dto.Telefone,
            Celular = dto.Celular,
            Matricula = dto.Matricula,
            Cargo = dto.Cargo,
            Funcao = dto.Funcao,
            Setor = dto.Setor,
            Lotacao = dto.Lotacao,
            Unidade = dto.Unidade,
            TipoVinculo = dto.TipoVinculo,
            ConselhoProfissional = dto.ConselhoProfissional,
            NumeroConselho = dto.NumeroConselho,
            UfConselho = dto.UfConselho,
            Especialidade = dto.Especialidade,
            DataAdmissao = dto.DataAdmissao,
            SituacaoFuncional = dto.SituacaoFuncional,
            Observacao = dto.Observacao,
            EmailLogin = dto.EmailLogin,
            UserName = dto.UserName,
            IsActive = dto.IsActive,
            MustChangePassword = dto.MustChangePassword,
            MfaEnabled = dto.MfaEnabled,
            CertificateRequired = dto.CertificateRequired,
            CanSignWithIcp = dto.CanSignWithIcp,
            SecurityLevel = dto.SecurityLevel,
            SelectedRoleIds = dto.RoleIds
        };

        await ReloadEditLookupsAsync(vm, ct);
        ViewData["Title"] = "Editar Servidor / Usuário";
        ViewData["Subtitle"] = "Atualização de cadastro, acesso, perfis, sigilo e ICP-Brasil";
        return View(vm);
    }

    [HttpGet("DiagnoseEdit/{id:guid}")]
    [Authorize(Roles = AppRoles.Admin + ",ADMINISTRATOR")]
    public async Task<IActionResult> DiagnoseEdit(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var isAdmin =
            User.IsInRole(AppRoles.Admin) ||
            User.IsInRole("ADMIN") ||
            User.IsInRole("ADMINISTRATOR");
        if (!isAdmin) return Forbid();

        var tenantId = _currentUser.TenantId;
        var diagnosisJson = await _repo.DiagnoseUserEditIdAsync(tenantId, id, ct);
        return Content(diagnosisJson, "application/json");
    }

    [HttpPost("Edit/{servidorId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid servidorId, [FromForm] EditUserVM vm, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var routeMatchesServidor = vm.ServidorId != Guid.Empty && servidorId == vm.ServidorId;
        var routeMatchesUserWithoutServidor = vm.ServidorId == Guid.Empty && vm.UserId.HasValue && vm.UserId.Value != Guid.Empty && servidorId == vm.UserId.Value;
        if (!routeMatchesServidor && !routeMatchesUserWithoutServidor)
        {
            TempData["Error"] = "Identificador do cadastro inválido.";
            return RedirectToAction(nameof(Index));
        }

        NormalizeEditVm(vm);
        ValidateEditVm(vm);
        var cpfDigits = OnlyDigits(vm.Cpf);
        var cpfInformado = !string.IsNullOrWhiteSpace(cpfDigits);

        if (!ModelState.IsValid)
        {
            await ReloadEditLookupsAsync(vm, ct);
            return View(vm);
        }

        var tenantId = _currentUser.TenantId;
        _logger.LogInformation(
            "Edição de usuário/servidor iniciada. Tenant={TenantId} Admin={AdminUserId} Servidor={ServidorId} User={UserId} CpfInformado={CpfInformado} CorrelationId={CorrelationId}",
            tenantId,
            _currentUser.UserId,
            vm.ServidorId,
            vm.UserId,
            cpfInformado,
            HttpContext.TraceIdentifier);

        try
        {
            if (cpfInformado && await _repo.CpfExistsAsync(tenantId, vm.Cpf, vm.ServidorId, ct))
            {
                ModelState.AddModelError(nameof(vm.Cpf), "Já existe outro servidor ativo cadastrado com este CPF.");
                await ReloadEditLookupsAsync(vm, ct);
                return View(vm);
            }

            if ((vm.UserId.HasValue || vm.CriarUsuarioAcesso) && await _repo.EmailExistsAsync(tenantId, vm.EmailLogin, vm.UserId, ct))
            {
                ModelState.AddModelError(nameof(vm.EmailLogin), "Já existe outro usuário com este e-mail de login.");
                await ReloadEditLookupsAsync(vm, ct);
                return View(vm);
            }

            var command = new UpdateServidorUsuarioCommand
            {
                UserId = vm.UserId,
                ServidorId = vm.ServidorId,
                CriarUsuarioAcesso = vm.CriarUsuarioAcesso,
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
                EmailLogin = vm.EmailLogin,
                UserName = vm.UserName,
                IsActive = vm.IsActive,
                MustChangePassword = vm.MustChangePassword,
                MfaEnabled = vm.MfaEnabled,
                CertificateRequired = vm.CertificateRequired,
                CanSignWithIcp = vm.CanSignWithIcp,
                SecurityLevel = vm.SecurityLevel,
                RoleIds = (vm.UserId.HasValue || vm.CriarUsuarioAcesso) ? vm.SelectedRoleIds.Where(x => x != Guid.Empty).Distinct().ToList() : new List<Guid>(),
                UpdatedBy = _currentUser.UserId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                CorrelationId = HttpContext.TraceIdentifier
            };

            await _repo.UpdateServidorUsuarioAsync(tenantId, command, ct);
            await _auditWriter.WriteAsync(tenantId, _currentUser.UserId, "UPDATE", "USER_ADMIN", vm.ServidorId != Guid.Empty ? vm.ServidorId : vm.UserId, "Cadastro de usuário/servidor atualizado.", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { vm.ServidorId, vm.UserId, CpfInformado = cpfInformado, CorrelationId = HttpContext.TraceIdentifier }, ct);
            TempData["Success"] = "Cadastro do servidor/usuário atualizado com sucesso.";
            return RedirectToAction(nameof(Index));
        }
        catch (PostgresException pex) when (pex.SqlState == "23505")
        {
            _logger.LogWarning(pex, "Duplicidade ao editar usuário | Tenant={TenantId} UserId={UserId}", tenantId, vm.UserId);
            ModelState.AddModelError("", "Já existe cadastro com o mesmo CPF, matrícula ou e-mail.");
            await ReloadEditLookupsAsync(vm, ct);
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao editar usuário | UserId={UserId}", vm.UserId);
            TempData["Error"] = "Erro ao editar usuário. Verifique os logs.";
            await ReloadEditLookupsAsync(vm, ct);
            return View(vm);
        }
    }

    [HttpPost("SetActive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid? id, bool active, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        if (!id.HasValue || id == Guid.Empty){ TempData["Error"] = "Este servidor ainda não possui usuário de acesso."; return RedirectToAction(nameof(Index)); }
        await _repo.SetActiveAsync(_currentUser.TenantId, id.Value, active, _currentUser.UserId, ct);
        TempData["Success"] = active ? "Usuário ativado com sucesso." : "Usuário inativado com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("ResetPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(Guid? id, string newPassword, string confirmPassword, bool mustChangePassword = true, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

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

        if (!id.HasValue || id == Guid.Empty){ TempData["Error"] = "Este servidor ainda não possui usuário de acesso."; return RedirectToAction(nameof(Index)); }

        var hasher = new PasswordHasher<object>();
        var hash = hasher.HashPassword(null!, newPassword);

        await _repo.ResetPasswordAsync(_currentUser.TenantId, id.Value, hash, mustChangePassword, _currentUser.UserId, ct);
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
        List<CreateUserVM.SelectItem> Map(string code, string? current = null)
        {
            var list = options.Where(x => x.CategoryCode == code).Select(x => new CreateUserVM.SelectItem(x.Value, x.Text)).ToList();
            EnsureSelected(list, current);
            return list;
        }

        vm.Cargos = Map("CARGO", vm.Cargo);
        vm.Funcoes = Map("FUNCAO", vm.Funcao);
        vm.Setores = Map("SETOR", vm.Setor);
        vm.Lotacoes = Map("LOTACAO", vm.Lotacao);
        vm.Unidades = Map("UNIDADE", vm.Unidade);
        vm.TiposVinculo = Map("TIPO_VINCULO", vm.TipoVinculo);
        vm.ConselhosProfissionais = Map("CONSELHO_PROFISSIONAL", vm.ConselhoProfissional);
        vm.Especialidades = Map("ESPECIALIDADE", vm.Especialidade);
        vm.SituacoesFuncionais = Map("SITUACAO_FUNCIONAL", vm.SituacaoFuncional);
        vm.SecurityLevels = Map("NIVEL_SIGILO", vm.SecurityLevel);
    }

    private static void FillEditOptions(EditUserVM vm, IReadOnlyList<ParameterSelectOption> options)
    {
        List<EditUserVM.SelectItem> Map(string code, string? current = null)
        {
            var list = options.Where(x => x.CategoryCode == code).Select(x => new EditUserVM.SelectItem(x.Value, x.Text)).ToList();
            EnsureSelected(list, current);
            return list;
        }

        vm.Cargos = Map("CARGO", vm.Cargo);
        vm.Funcoes = Map("FUNCAO", vm.Funcao);
        vm.Setores = Map("SETOR", vm.Setor);
        vm.Lotacoes = Map("LOTACAO", vm.Lotacao);
        vm.Unidades = Map("UNIDADE", vm.Unidade);
        vm.TiposVinculo = Map("TIPO_VINCULO", vm.TipoVinculo);
        vm.ConselhosProfissionais = Map("CONSELHO_PROFISSIONAL", vm.ConselhoProfissional);
        vm.Especialidades = Map("ESPECIALIDADE", vm.Especialidade);
        vm.SituacoesFuncionais = Map("SITUACAO_FUNCIONAL", vm.SituacaoFuncional);
        vm.SecurityLevels = Map("NIVEL_SIGILO", vm.SecurityLevel);
    }

    private static void EnsureSelected<T>(List<T> list, string? current) where T : class
    {
        if (string.IsNullOrWhiteSpace(current)) return;

        var valueProp = typeof(T).GetProperty("Value");
        var textProp = typeof(T).GetProperty("Text");
        if (valueProp is null || textProp is null) return;

        var exists = list.Any(x => string.Equals(valueProp.GetValue(x)?.ToString(), current, StringComparison.OrdinalIgnoreCase));
        if (exists) return;

        var item = Activator.CreateInstance<T>();
        valueProp.SetValue(item, current);
        textProp.SetValue(item, current + " (valor atual)");
        list.Insert(0, item);
    }

    private void ValidateCreateVm(CreateUserVM vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Cpf) || OnlyDigits(vm.Cpf).Length != 11)
            ModelState.AddModelError(nameof(vm.Cpf), "CPF inválido. Informe 11 dígitos.");

        if (vm.CriarUsuarioAcesso)
        {
            if (string.IsNullOrWhiteSpace(vm.EmailLogin)) ModelState.AddModelError(nameof(vm.EmailLogin), "Informe o e-mail de login.");
            if (string.IsNullOrWhiteSpace(vm.Password)) ModelState.AddModelError(nameof(vm.Password), "Informe a senha inicial.");
            if (vm.Password?.Length < 8) ModelState.AddModelError(nameof(vm.Password), "A senha deve possuir pelo menos 8 caracteres.");
            if (vm.Password != vm.ConfirmPassword) ModelState.AddModelError(nameof(vm.ConfirmPassword), "A confirmação não confere com a senha.");
            if (vm.SelectedRoleIds is null || vm.SelectedRoleIds.Count == 0) ModelState.AddModelError(nameof(vm.SelectedRoleIds), "Selecione ao menos um perfil de acesso.");
        }
    }

    private void ValidateEditVm(EditUserVM vm)
    {
        if (vm.ServidorId == Guid.Empty && (!vm.UserId.HasValue || vm.UserId.Value == Guid.Empty)) ModelState.AddModelError(nameof(vm.ServidorId), "Cadastro sem identificador válido para edição.");
        var cpfDigits = OnlyDigits(vm.Cpf);
        if (!string.IsNullOrWhiteSpace(cpfDigits) && cpfDigits.Length != 11) ModelState.AddModelError(nameof(vm.Cpf), "CPF inválido. Informe 11 dígitos ou deixe em branco.");
        var hasAccess = vm.UserId.HasValue && vm.UserId.Value != Guid.Empty;
        var shouldValidateAccess = hasAccess || vm.CriarUsuarioAcesso;
        if (shouldValidateAccess)
        {
            if (string.IsNullOrWhiteSpace(vm.EmailLogin)) ModelState.AddModelError(nameof(vm.EmailLogin), "Informe o e-mail de login.");
            if (vm.SelectedRoleIds is null || vm.SelectedRoleIds.Count == 0) ModelState.AddModelError(nameof(vm.SelectedRoleIds), "Selecione ao menos um perfil de acesso.");
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
        vm.SituacaoFuncional = string.IsNullOrWhiteSpace(vm.SituacaoFuncional) ? "ATIVO" : vm.SituacaoFuncional.Trim().ToUpperInvariant();
        vm.SecurityLevel = string.IsNullOrWhiteSpace(vm.SecurityLevel) ? "PUBLIC" : vm.SecurityLevel.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(vm.EmailLogin)) vm.EmailLogin = vm.EmailInstitucional ?? vm.EmailAlternativo ?? "";
        if (vm.CriarUsuarioAcesso && string.IsNullOrWhiteSpace(vm.UserName)) vm.UserName = vm.EmailLogin;
    }

    private static void NormalizeEditVm(EditUserVM vm)
    {
        vm.NomeCompleto = Trim(vm.NomeCompleto);
        var cpfDigits = OnlyDigits(vm.Cpf);
        vm.Cpf = string.IsNullOrWhiteSpace(cpfDigits)
            ? string.Empty
            : FormatCpf(cpfDigits);
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
        vm.SituacaoFuncional = string.IsNullOrWhiteSpace(vm.SituacaoFuncional) ? "ATIVO" : vm.SituacaoFuncional.Trim().ToUpperInvariant();
        vm.SecurityLevel = string.IsNullOrWhiteSpace(vm.SecurityLevel) ? "PUBLIC" : vm.SecurityLevel.Trim().ToUpperInvariant();
        if ((vm.UserId.HasValue || vm.CriarUsuarioAcesso) && string.IsNullOrWhiteSpace(vm.UserName)) vm.UserName = vm.EmailLogin;
    }

    private static string Trim(string? value) => (value ?? "").Trim();
    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? TrimLowerOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    private static string OnlyDigits(string? value) => new string((value ?? "").Where(char.IsDigit).ToArray());

    private static string FormatCpf(string? value)
    {
        var digits = OnlyDigits(value);
        if (digits.Length != 11) return value?.Trim() ?? "";
        return Convert.ToUInt64(digits).ToString(@"000\.000\.000\-00");
    }
}
