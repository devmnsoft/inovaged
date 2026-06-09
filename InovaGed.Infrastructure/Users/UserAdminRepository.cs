using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Users;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Users;

public sealed class UserAdminRepository : IUserAdminRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<UserAdminRepository> _logger;

    public UserAdminRepository(
        IDbConnectionFactory db,
        ILogger<UserAdminRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RoleRowDto>> ListRolesAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT id AS ""Id"", name AS ""Name""
FROM ged.app_role
WHERE tenant_id = @TenantId
ORDER BY lower(name);
";

        await using var con = await _db.OpenAsync(ct);

        var rows = await con.QueryAsync<RoleRowDto>(
            new CommandDefinition(
                sql,
                new { TenantId = tenantId },
                cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<ServidorUsuarioCreatedDto> CreateServidorUsuarioAsync(
        Guid tenantId,
        CreateServidorUsuarioCommand command,
        CancellationToken ct)
    {
        const string insertServidorSql = @"
INSERT INTO ged.servidor (
    id,
    tenant_id,
    nome_completo,
    cpf,
    rg,
    data_nascimento,
    email_institucional,
    email_alternativo,
    telefone,
    celular,
    matricula,
    cargo,
    funcao,
    setor,
    lotacao,
    unidade,
    tipo_vinculo,
    conselho_profissional,
    numero_conselho,
    uf_conselho,
    especialidade,
    data_admissao,
    situacao_funcional,
    observacao,
    created_by,
    created_at,
    reg_status
)
VALUES (
    @Id,
    @TenantId,
    @NomeCompleto,
    @Cpf,
    @Rg,
    @DataNascimento::date,
    @EmailInstitucional,
    @EmailAlternativo,
    @Telefone,
    @Celular,
    @Matricula,
    @Cargo,
    @Funcao,
    @Setor,
    @Lotacao,
    @Unidade,
    @TipoVinculo,
    @ConselhoProfissional,
    @NumeroConselho,
    @UfConselho,
    @Especialidade,
    @DataAdmissao::date,
    @SituacaoFuncional,
    @Observacao,
    @CreatedBy,
    now(),
    'A'
);
";

        const string updateServidorSql = @"
UPDATE ged.servidor
SET
    nome_completo = @NomeCompleto,
    cpf = @Cpf,
    rg = @Rg,
    data_nascimento = @DataNascimento::date,
    email_institucional = @EmailInstitucional,
    email_alternativo = @EmailAlternativo,
    telefone = @Telefone,
    celular = @Celular,
    matricula = @Matricula,
    cargo = @Cargo,
    funcao = @Funcao,
    setor = @Setor,
    lotacao = @Lotacao,
    unidade = @Unidade,
    tipo_vinculo = @TipoVinculo,
    conselho_profissional = @ConselhoProfissional,
    numero_conselho = @NumeroConselho,
    uf_conselho = @UfConselho,
    especialidade = @Especialidade,
    data_admissao = @DataAdmissao::date,
    situacao_funcional = @SituacaoFuncional,
    observacao = @Observacao,
    updated_by = @CreatedBy,
    updated_at = now()
WHERE tenant_id = @TenantId
  AND id = @Id
  AND reg_status = 'A';
";

        const string insertUserSql = @"
INSERT INTO ged.app_user (
    id,
    tenant_id,
    servidor_id,
    name,
    email,
    normalized_email,
    user_name,
    normalized_user_name,
    phone_number,
    password_hash,
    is_active,
    must_change_password,
    mfa_enabled,
    certificate_required,
    can_sign_with_icp,
    security_level,
    created_at,
    last_password_change_at
)
VALUES (
    @Id,
    @TenantId,
    @ServidorId,
    @Name,
    @Email,
    @NormalizedEmail,
    @UserName,
    @NormalizedUserName,
    @PhoneNumber,
    @PasswordHash,
    @IsActive,
    @MustChangePassword,
    @MfaEnabled,
    @CertificateRequired,
    @CanSignWithIcp,
    @SecurityLevel::ged.security_level,
    now(),
    now()
);
";

        const string insertRoleSql = @"
INSERT INTO ged.user_role (user_id, role_id)
VALUES (@UserId, @RoleId)
ON CONFLICT DO NOTHING;
";

        const string auditSql = @"
SELECT ged.audit_user_security_event(
    @TenantId,
    @UserId,
    @ServidorId,
    @EventType,
    @EventDescription,
    @CreatedBy,
    @IpAddress,
    @UserAgent,
    @CorrelationId,
    @Data::jsonb
);
";

        try
        {
            await using var con = await _db.OpenAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);

            var servidorId = command.ServidorId.GetValueOrDefault(Guid.NewGuid());

            var servidorParams = new
            {
                Id = servidorId,
                TenantId = tenantId,
                NomeCompleto = command.NomeCompleto.Trim(),
                Cpf = NormalizeCpf(command.Cpf),
                Rg = TrimOrNull(command.Rg),
                DataNascimento = command.DataNascimento?.Date,
                EmailInstitucional = TrimLowerOrNull(command.EmailInstitucional),
                EmailAlternativo = TrimLowerOrNull(command.EmailAlternativo),
                Telefone = TrimOrNull(command.Telefone),
                Celular = TrimOrNull(command.Celular),
                Matricula = TrimOrNull(command.Matricula),
                Cargo = TrimOrNull(command.Cargo),
                Funcao = TrimOrNull(command.Funcao),
                Setor = TrimOrNull(command.Setor),
                Lotacao = TrimOrNull(command.Lotacao),
                Unidade = TrimOrNull(command.Unidade),
                TipoVinculo = TrimOrNull(command.TipoVinculo),
                ConselhoProfissional = TrimOrNull(command.ConselhoProfissional),
                NumeroConselho = TrimOrNull(command.NumeroConselho),
                UfConselho = TrimUpperOrNull(command.UfConselho),
                Especialidade = TrimOrNull(command.Especialidade),
                DataAdmissao = command.DataAdmissao?.Date,
                SituacaoFuncional = string.IsNullOrWhiteSpace(command.SituacaoFuncional)
                    ? "ATIVO"
                    : command.SituacaoFuncional.Trim().ToUpperInvariant(),
                Observacao = TrimOrNull(command.Observacao),
                CreatedBy = command.CreatedBy
            };

            if (command.ServidorId.HasValue && command.ServidorId.Value != Guid.Empty)
            {
                await con.ExecuteAsync(
                    new CommandDefinition(
                        updateServidorSql,
                        servidorParams,
                        transaction: tx,
                        cancellationToken: ct));
            }
            else
            {
                await con.ExecuteAsync(
                    new CommandDefinition(
                        insertServidorSql,
                        servidorParams,
                        transaction: tx,
                        cancellationToken: ct));
            }

            Guid? userId = null;

            if (command.CriarUsuarioAcesso)
            {
                userId = Guid.NewGuid();

                var emailLogin = TrimLowerOrNull(command.EmailLogin);
                if (string.IsNullOrWhiteSpace(emailLogin))
                    throw new InvalidOperationException("E-mail de login é obrigatório para criar usuário de acesso.");

                var userName = TrimOrNull(command.UserName) ?? emailLogin;

                await con.ExecuteAsync(
                    new CommandDefinition(
                        insertUserSql,
                        new
                        {
                            Id = userId.Value,
                            TenantId = tenantId,
                            ServidorId = servidorId,
                            Name = command.NomeCompleto.Trim(),
                            Email = emailLogin,
                            NormalizedEmail = emailLogin.ToUpperInvariant(),
                            UserName = userName,
                            NormalizedUserName = userName.ToUpperInvariant(),
                            PhoneNumber = TrimOrNull(command.Celular) ?? TrimOrNull(command.Telefone),
                            PasswordHash = command.PasswordHash,
                            IsActive = command.IsActive,
                            MustChangePassword = command.MustChangePassword,
                            MfaEnabled = command.MfaEnabled,
                            CertificateRequired = command.CertificateRequired,
                            CanSignWithIcp = command.CanSignWithIcp,
                            SecurityLevel = NormalizeSecurityLevel(command.SecurityLevel)
                        },
                        transaction: tx,
                        cancellationToken: ct));

                foreach (var roleId in command.RoleIds.Where(x => x != Guid.Empty).Distinct())
                {
                    await con.ExecuteAsync(
                        new CommandDefinition(
                            insertRoleSql,
                            new
                            {
                                UserId = userId.Value,
                                RoleId = roleId
                            },
                            transaction: tx,
                            cancellationToken: ct));
                }
            }

            await con.ExecuteAsync(
                new CommandDefinition(
                    auditSql,
                    new
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        ServidorId = servidorId,
                        EventType = "USER_CREATE",
                        EventDescription = userId.HasValue
                            ? "Servidor cadastrado e usuário de acesso criado."
                            : "Servidor cadastrado sem usuário de acesso.",
                        CreatedBy = command.CreatedBy,
                        IpAddress = command.IpAddress,
                        UserAgent = command.UserAgent,
                        CorrelationId = command.CorrelationId,
                        Data = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            command.NomeCompleto,
                            Cpf = NormalizeCpf(command.Cpf),
                            command.Matricula,
                            command.Setor,
                            command.Cargo,
                            command.Funcao,
                            command.SecurityLevel,
                            Roles = command.RoleIds
                        })
                    },
                    transaction: tx,
                    cancellationToken: ct));

            await tx.CommitAsync(ct);

            return new ServidorUsuarioCreatedDto
            {
                ServidorId = servidorId,
                UserId = userId
            };
        }
        catch (PostgresException pex) when (pex.SqlState == "23505")
        {
            _logger.LogWarning(
                pex,
                "Duplicidade ao criar servidor/usuário | Tenant={TenantId} CPF={Cpf} Email={Email}",
                tenantId,
                command.Cpf,
                command.EmailLogin);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro CreateServidorUsuarioAsync | Tenant={TenantId} CPF={Cpf} Email={Email}",
                tenantId,
                command.Cpf,
                command.EmailLogin);

            throw;
        }
    }

    public async Task SetActiveAsync(
        Guid tenantId,
        Guid userId,
        bool isActive,
        Guid? changedBy,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET is_active = @IsActive,
    updated_at_utc = now()
WHERE tenant_id = @TenantId
  AND id = @UserId
  AND deleted_at_utc IS NULL;
";

        const string auditSql = @"
SELECT ged.audit_user_security_event(
    @TenantId,
    @UserId,
    NULL,
    @EventType,
    @EventDescription,
    @ChangedBy,
    NULL,
    NULL,
    NULL,
    @Data::jsonb
);
";

        await using var con = await _db.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        await con.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    UserId = userId,
                    IsActive = isActive
                },
                transaction: tx,
                cancellationToken: ct));

        await con.ExecuteAsync(
            new CommandDefinition(
                auditSql,
                new
                {
                    TenantId = tenantId,
                    UserId = userId,
                    EventType = isActive ? "USER_ACTIVATE" : "USER_DEACTIVATE",
                    EventDescription = isActive ? "Usuário ativado." : "Usuário inativado.",
                    ChangedBy = changedBy,
                    Data = System.Text.Json.JsonSerializer.Serialize(new { isActive })
                },
                transaction: tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task ResetPasswordAsync(
        Guid tenantId,
        Guid userId,
        string newPasswordHash,
        bool mustChangePassword,
        Guid? changedBy,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET password_hash = @Hash,
    must_change_password = @MustChangePassword,
    failed_access_count = 0,
    is_locked = false,
    locked_until = NULL,
    password_reset_token_hash = NULL,
    password_reset_expires_at = NULL,
    password_reset_used_at = now(),
    last_password_change_at = now(),
    updated_at_utc = now()
WHERE tenant_id = @TenantId
  AND id = @UserId
  AND deleted_at_utc IS NULL;
";

        const string auditSql = @"
SELECT ged.audit_user_security_event(
    @TenantId,
    @UserId,
    NULL,
    'PASSWORD_RESET_ADMIN',
    'Senha redefinida administrativamente.',
    @ChangedBy,
    NULL,
    NULL,
    NULL,
    @Data::jsonb
);
";

        await using var con = await _db.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        await con.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    UserId = userId,
                    Hash = newPasswordHash,
                    MustChangePassword = mustChangePassword
                },
                transaction: tx,
                cancellationToken: ct));

        await con.ExecuteAsync(
            new CommandDefinition(
                auditSql,
                new
                {
                    TenantId = tenantId,
                    UserId = userId,
                    ChangedBy = changedBy,
                    Data = System.Text.Json.JsonSerializer.Serialize(new { mustChangePassword })
                },
                transaction: tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task<bool> EmailExistsAsync(
        Guid tenantId,
        string email,
        Guid? ignoreUserId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT EXISTS (
    SELECT 1
    FROM ged.app_user
    WHERE tenant_id = @TenantId
      AND normalized_email = upper(@Email)
      AND deleted_at_utc IS NULL
      AND (@IgnoreUserId IS NULL OR id <> @IgnoreUserId)
);
";

        await using var con = await _db.OpenAsync(ct);

        return await con.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    Email = email.Trim(),
                    IgnoreUserId = ignoreUserId
                },
                cancellationToken: ct));
    }

    public async Task<bool> CpfExistsAsync(
        Guid tenantId,
        string? cpf,
        Guid? ignoreServidorId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cpf))
            return false;

        var digits = new string(cpf.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
            return false;

        const string sql = @"
SELECT EXISTS (
    SELECT 1
    FROM ged.servidor
    WHERE tenant_id = @TenantId
      AND regexp_replace(cpf, '\D', '', 'g') = regexp_replace(@Cpf, '\D', '', 'g')
      AND reg_status = 'A'
      AND (@IgnoreServidorId IS NULL OR id <> @IgnoreServidorId)
);
";

        await using var con = await _db.OpenAsync(ct);

        return await con.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    Cpf = digits,
                    IgnoreServidorId = ignoreServidorId
                },
                cancellationToken: ct));
    }

    public async Task<bool> UnlockUserAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.app_user
SET is_locked = FALSE,
    access_failed_count = 0,
    locked_until = NULL,
    updated_at = now()
WHERE tenant_id = @TenantId
  AND id = @UserId
  AND deleted_at_utc IS NULL;
";

        await using var con = await _db.OpenAsync(ct);
        var affected = await con.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { TenantId = tenantId, UserId = userId },
                cancellationToken: ct));

        return affected > 0;
    }

    private static string NormalizeCpf(string cpf)
    {
        var digits = new string((cpf ?? "").Where(char.IsDigit).ToArray());

        if (digits.Length == 11)
        {
            return Convert.ToUInt64(digits).ToString(@"000\.000\.000\-00");
        }

        return cpf.Trim();
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TrimLowerOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private static string? TrimUpperOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeSecurityLevel(string? value)
    {
        var normalized = (value ?? "PUBLIC").Trim().ToUpperInvariant();

        return normalized switch
        {
            "PUBLIC" => "PUBLIC",
            "RESTRICTED" => "RESTRICTED",
            "CONFIDENTIAL" => "CONFIDENTIAL",
            _ => "PUBLIC"
        };
    }


    public async Task<UserEditDto?> GetForEditByServidorIdAsync(
     Guid tenantId,
     Guid servidorId,
     bool isAdmin,
     CancellationToken ct)
    {
        const string sql = @"
SELECT
    u.id                         AS ""UserId"",
    COALESCE(s.id, u.servidor_id, '00000000-0000-0000-0000-000000000000'::uuid) AS ""ServidorId"",
    COALESCE(s.nome_completo, u.name, '') AS ""NomeCompleto"",
    COALESCE(s.cpf, '')                   AS ""Cpf"",
    s.rg                                  AS ""Rg"",
    s.data_nascimento::timestamp          AS ""DataNascimento"",
    COALESCE(s.email_institucional, u.email) AS ""EmailInstitucional"",
    s.email_alternativo                      AS ""EmailAlternativo"",
    s.telefone                               AS ""Telefone"",
    COALESCE(s.celular, u.phone_number)      AS ""Celular"",
    s.matricula AS ""Matricula"", s.cargo AS ""Cargo"", s.funcao AS ""Funcao"", s.setor AS ""Setor"", s.lotacao AS ""Lotacao"", s.unidade AS ""Unidade"", s.tipo_vinculo AS ""TipoVinculo"",
    s.conselho_profissional AS ""ConselhoProfissional"", s.numero_conselho AS ""NumeroConselho"", s.uf_conselho AS ""UfConselho"", s.especialidade AS ""Especialidade"",
    s.data_admissao::timestamp AS ""DataAdmissao"", COALESCE(s.situacao_funcional, 'ATIVO') AS ""SituacaoFuncional"", s.observacao AS ""Observacao"",
    COALESCE(u.email,'') AS ""EmailLogin"", u.user_name AS ""UserName"",
    COALESCE(u.is_active, false) AS ""IsActive"", COALESCE(u.must_change_password, true) AS ""MustChangePassword"", COALESCE(u.mfa_enabled, false) AS ""MfaEnabled"", COALESCE(u.certificate_required, false) AS ""CertificateRequired"", COALESCE(u.can_sign_with_icp, false) AS ""CanSignWithIcp"", COALESCE(u.security_level::text, 'PUBLIC') AS ""SecurityLevel""
FROM ged.servidor s
LEFT JOIN ged.app_user u ON u.servidor_id=s.id AND u.tenant_id=s.tenant_id AND u.deleted_at_utc IS NULL
WHERE s.tenant_id=@TenantId
  AND s.id=@ServidorId
  AND (@IsAdmin = TRUE OR COALESCE(s.reg_status, 'A') = 'A')
LIMIT 1;";
        const string rolesSql = @"SELECT role_id FROM ged.user_role WHERE user_id = @UserId;";
        await using var con = await _db.OpenAsync(ct);
        _logger.LogInformation("Consultando cadastro para edição por ServidorId. TenantId={TenantId} ServidorId={ServidorId} IsAdmin={IsAdmin}", tenantId, servidorId, isAdmin);
        var dto = await con.QueryFirstOrDefaultAsync<UserEditDto>(new CommandDefinition(sql, new { TenantId = tenantId, ServidorId = servidorId, IsAdmin = isAdmin }, cancellationToken: ct));
        if (dto is null) return null;
        _logger.LogInformation("Cadastro para edição encontrado. TenantId={TenantId} ServidorId={ServidorId} UserId={UserId}", tenantId, dto.ServidorId, dto.UserId);
        if (dto.UserId.HasValue)
        {
            var roles = await con.QueryAsync<Guid>(new CommandDefinition(rolesSql, new { UserId = dto.UserId.Value }, cancellationToken: ct));
            dto.RoleIds = roles.ToList();
            _logger.LogInformation("Perfis carregados para edição. TenantId={TenantId} ServidorId={ServidorId} UserId={UserId} RolesCount={RolesCount}", tenantId, dto.ServidorId, dto.UserId, dto.RoleIds.Count);
        }
        return dto;
    }

    public async Task<UserEditDto?> GetForEditByUserIdAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    u.id                         AS ""UserId"",
    COALESCE(s.id, u.servidor_id, '00000000-0000-0000-0000-000000000000'::uuid) AS ""ServidorId"",
    COALESCE(s.nome_completo, u.name, '') AS ""NomeCompleto"",
    COALESCE(s.cpf, '')                   AS ""Cpf"",
    s.rg                                  AS ""Rg"",
    s.data_nascimento::timestamp          AS ""DataNascimento"",
    COALESCE(s.email_institucional, u.email) AS ""EmailInstitucional"",
    s.email_alternativo                      AS ""EmailAlternativo"",
    s.telefone                               AS ""Telefone"",
    COALESCE(s.celular, u.phone_number)      AS ""Celular"",
    s.matricula AS ""Matricula"", s.cargo AS ""Cargo"", s.funcao AS ""Funcao"", s.setor AS ""Setor"", s.lotacao AS ""Lotacao"", s.unidade AS ""Unidade"", s.tipo_vinculo AS ""TipoVinculo"",
    s.conselho_profissional AS ""ConselhoProfissional"", s.numero_conselho AS ""NumeroConselho"", s.uf_conselho AS ""UfConselho"", s.especialidade AS ""Especialidade"",
    s.data_admissao::timestamp AS ""DataAdmissao"", COALESCE(s.situacao_funcional, 'ATIVO') AS ""SituacaoFuncional"", s.observacao AS ""Observacao"",
    COALESCE(u.email,'') AS ""EmailLogin"", u.user_name AS ""UserName"",
    COALESCE(u.is_active, false) AS ""IsActive"", COALESCE(u.must_change_password, true) AS ""MustChangePassword"", COALESCE(u.mfa_enabled, false) AS ""MfaEnabled"", COALESCE(u.certificate_required, false) AS ""CertificateRequired"", COALESCE(u.can_sign_with_icp, false) AS ""CanSignWithIcp"", COALESCE(u.security_level::text, 'PUBLIC') AS ""SecurityLevel""
FROM ged.app_user u
LEFT JOIN ged.servidor s ON s.id=u.servidor_id AND s.tenant_id=u.tenant_id AND s.reg_status='A'
WHERE u.tenant_id=@TenantId
  AND u.id=@UserId
  AND u.deleted_at_utc IS NULL
LIMIT 1;";
        const string rolesSql = @"SELECT role_id FROM ged.user_role WHERE user_id = @UserId;";

        await using var con = await _db.OpenAsync(ct);
        var dto = await con.QueryFirstOrDefaultAsync<UserEditDto>(
            new CommandDefinition(sql, new { TenantId = tenantId, UserId = userId }, cancellationToken: ct));
        if (dto is null)
            return null;

        var roles = await con.QueryAsync<Guid>(new CommandDefinition(rolesSql, new { UserId = userId }, cancellationToken: ct));
        dto.RoleIds = roles.ToList();
        return dto;
    }

    public async Task<UserEditDto?> GetForEditFromAdminListAsync(
        Guid tenantId,
        Guid id,
        Guid? adminId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    u.servidor_id AS ""ServidorId"",
    u.user_id AS ""UserId"",
    COALESCE(u.nome_completo, '') AS ""NomeCompleto"",
    COALESCE(u.cpf, '') AS ""Cpf"",
    u.matricula AS ""Matricula"",
    u.cargo AS ""Cargo"",
    u.funcao AS ""Funcao"",
    u.setor AS ""Setor"",
    u.lotacao AS ""Lotacao"",
    u.unidade AS ""Unidade"",
    COALESCE(u.email, '') AS ""EmailLogin"",
    COALESCE(u.is_active, false) AS ""IsActive"",
    COALESCE(u.is_locked, false) AS ""IsLocked"",
    COALESCE(u.must_change_password, true) AS ""MustChangePassword"",
    COALESCE(u.mfa_enabled, false) AS ""MfaEnabled"",
    COALESCE(u.certificate_required, false) AS ""CertificateRequired"",
    COALESCE(u.can_sign_with_icp, false) AS ""CanSignWithIcp"",
    COALESCE(u.security_level, 'PUBLIC') AS ""SecurityLevel""
FROM ged.vw_user_admin_list u
WHERE u.tenant_id = @TenantId
  AND (u.servidor_id = @Id OR u.user_id = @Id)
LIMIT 1;";
        const string rolesSql = @"SELECT role_id FROM ged.user_role WHERE user_id = @UserId;";

        await using var con = await _db.OpenAsync(ct);
        if (adminId.HasValue)
        {
            var repairedServidorId = await RepairServidorFromAdminListAsync(tenantId, id, adminId.Value, ct);
            _logger.LogInformation(
                "Reparo automático por vw_user_admin_list antes do fallback de edição. TenantId={TenantId} RouteId={RouteId} AdminId={AdminId} RepairedServidorId={RepairedServidorId}",
                tenantId,
                id,
                adminId,
                repairedServidorId);
        }

        var dto = await con.QueryFirstOrDefaultAsync<UserEditDto>(
            new CommandDefinition(sql, new { TenantId = tenantId, Id = id }, cancellationToken: ct));
        if (dto is null)
            return null;

        dto.UserName = dto.EmailLogin;
        if (dto.UserId.HasValue)
        {
            var roles = await con.QueryAsync<Guid>(new CommandDefinition(rolesSql, new { UserId = dto.UserId.Value }, cancellationToken: ct));
            dto.RoleIds = roles.ToList();
        }

        return dto;
    }

    public async Task<Guid?> EnsureServidorForUserAsync(
        Guid tenantId,
        Guid userId,
        Guid adminId,
        CancellationToken ct)
    {
        const string userSql = @"
SELECT
    id AS ""Id"",
    tenant_id AS ""TenantId"",
    servidor_id AS ""ServidorId"",
    name AS ""Name"",
    email AS ""Email"",
    user_name AS ""UserName"",
    phone_number AS ""PhoneNumber"",
    is_active AS ""IsActive""
FROM ged.app_user
WHERE tenant_id = @TenantId
  AND id = @UserId
  AND deleted_at_utc IS NULL
LIMIT 1;";

        const string existsServidorSql = @"
SELECT EXISTS(
    SELECT 1
    FROM ged.servidor
    WHERE tenant_id = @TenantId
      AND id = @ServidorId
);";

        const string insertServidorSql = @"
INSERT INTO ged.servidor
(
    id,
    tenant_id,
    nome_completo,
    cpf,
    email_institucional,
    celular,
    situacao_funcional,
    created_by,
    created_at,
    reg_status
)
VALUES
(
    @ServidorId,
    @TenantId,
    @NomeCompleto,
    @Cpf,
    @EmailInstitucional,
    @Celular,
    'ATIVO',
    @CreatedBy,
    now(),
    'A'
);";

        const string updateUserSql = @"
UPDATE ged.app_user
SET servidor_id = @ServidorId,
    updated_at_utc = now()
WHERE tenant_id = @TenantId
  AND id = @UserId
  AND deleted_at_utc IS NULL;";

        await using var con = await _db.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        _logger.LogInformation(
            "Reparando vínculo servidor para usuário. Tenant={TenantId} UserId={UserId} AdminId={AdminId}",
            tenantId,
            userId,
            adminId);

        try
        {
            var user = await con.QueryFirstOrDefaultAsync<AppUserRepairRow>(
                new CommandDefinition(
                    userSql,
                    new { TenantId = tenantId, UserId = userId },
                    transaction: tx,
                    cancellationToken: ct));

            if (user is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            var servidorId = user.ServidorId.HasValue && user.ServidorId.Value != Guid.Empty
                ? user.ServidorId.Value
                : Guid.NewGuid();

            if (servidorId == Guid.Empty)
                servidorId = Guid.NewGuid();

            var servidorExists = await con.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    existsServidorSql,
                    new { TenantId = tenantId, ServidorId = servidorId },
                    transaction: tx,
                    cancellationToken: ct));

            _logger.LogInformation(
                "Servidor definido para reparo. Tenant={TenantId} UserId={UserId} ServidorId={ServidorId} Exists={Exists}",
                tenantId,
                userId,
                servidorId,
                servidorExists);

            if (!servidorExists)
            {
                await con.ExecuteAsync(
                    new CommandDefinition(
                        insertServidorSql,
                        new
                        {
                            TenantId = tenantId,
                            ServidorId = servidorId,
                            NomeCompleto = FirstNonBlank(user.Name, user.Email, user.UserName, "Servidor sem nome"),
                            Cpf = TechnicalCpf(servidorId),
                            EmailInstitucional = user.Email,
                            Celular = user.PhoneNumber,
                            CreatedBy = adminId
                        },
                        transaction: tx,
                        cancellationToken: ct));

                _logger.LogInformation(
                    "Servidor criado para reparar usuário sem vínculo. Tenant={TenantId} UserId={UserId} ServidorId={ServidorId}",
                    tenantId,
                    userId,
                    servidorId);
            }

            await con.ExecuteAsync(
                new CommandDefinition(
                    updateUserSql,
                    new
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        ServidorId = servidorId
                    },
                    transaction: tx,
                    cancellationToken: ct));

            await tx.CommitAsync(ct);
            return servidorId;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            _logger.LogError(ex,
                "Violação FK ao reparar servidor/user. Tenant={TenantId} UserId={UserId}",
                tenantId,
                userId);

            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao reparar vínculo servidor/user. Tenant={TenantId} UserId={UserId} AdminId={AdminId}",
                tenantId,
                userId,
                adminId);

            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Guid?> RepairServidorFromAdminListAsync(
        Guid tenantId,
        Guid id,
        Guid adminId,
        CancellationToken ct)
    {
        const string selectAdminListSql = @"
SELECT
    v.servidor_id AS ""ServidorId"",
    v.user_id AS ""UserId"",
    v.nome_completo AS ""NomeCompleto"",
    v.email AS ""Email"",
    v.cpf AS ""Cpf"",
    v.matricula AS ""Matricula"",
    v.cargo AS ""Cargo"",
    v.funcao AS ""Funcao"",
    v.setor AS ""Setor"",
    v.lotacao AS ""Lotacao"",
    v.unidade AS ""Unidade""
FROM ged.vw_user_admin_list v
WHERE v.tenant_id = @TenantId
  AND (v.servidor_id = @Id OR v.user_id = @Id)
LIMIT 1;";

        const string servidorExistsSql = @"
SELECT EXISTS(
    SELECT 1
    FROM ged.servidor s
    WHERE s.tenant_id = @TenantId
      AND s.id = @ServidorId
);";

        const string insertServidorSql = @"
INSERT INTO ged.servidor (
    id,
    tenant_id,
    nome_completo,
    cpf,
    email_institucional,
    matricula,
    cargo,
    funcao,
    setor,
    lotacao,
    unidade,
    situacao_funcional,
    created_by,
    created_at,
    reg_status
)
VALUES (
    @ServidorId,
    @TenantId,
    @NomeCompleto,
    @Cpf,
    @EmailInstitucional,
    @Matricula,
    @Cargo,
    @Funcao,
    @Setor,
    @Lotacao,
    @Unidade,
    'ATIVO',
    @AdminId,
    now(),
    'A'
)
ON CONFLICT (id) DO NOTHING;";

        await using var con = await _db.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        var row = await con.QueryFirstOrDefaultAsync<AdminListRepairRow>(
            new CommandDefinition(selectAdminListSql, new { TenantId = tenantId, Id = id }, tx, cancellationToken: ct));

        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var servidorId = row.ServidorId.GetValueOrDefault();
        if (servidorId == Guid.Empty && row.UserId.HasValue && row.UserId.Value != Guid.Empty)
        {
            await tx.RollbackAsync(ct);
            return await EnsureServidorForUserAsync(tenantId, row.UserId.Value, adminId, ct);
        }

        if (servidorId == Guid.Empty)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var servidorExists = await con.ExecuteScalarAsync<bool>(
            new CommandDefinition(servidorExistsSql, new { TenantId = tenantId, ServidorId = servidorId }, tx, cancellationToken: ct));

        if (!servidorExists)
        {
            await con.ExecuteAsync(new CommandDefinition(
                insertServidorSql,
                new
                {
                    TenantId = tenantId,
                    ServidorId = servidorId,
                    NomeCompleto = FirstNonBlank(row.NomeCompleto, row.Email, "Usuário sem nome"),
                    Cpf = string.IsNullOrWhiteSpace(row.Cpf) ? TechnicalCpf(servidorId) : row.Cpf,
                    EmailInstitucional = row.Email,
                    row.Matricula,
                    row.Cargo,
                    row.Funcao,
                    row.Setor,
                    row.Lotacao,
                    row.Unidade,
                    AdminId = adminId
                },
                tx,
                cancellationToken: ct));

            _logger.LogWarning(
                "Servidor inexistente listado pela vw_user_admin_list foi reparado. TenantId={TenantId} RouteId={RouteId} ServidorId={ServidorId} UserId={UserId} AdminId={AdminId}",
                tenantId,
                id,
                servidorId,
                row.UserId,
                adminId);
        }

        if (row.UserId.HasValue && row.UserId.Value != Guid.Empty)
        {
            await con.ExecuteAsync(new CommandDefinition(
                @"UPDATE ged.app_user
SET servidor_id = @ServidorId,
    updated_at_utc = now()
WHERE tenant_id = @TenantId
  AND id = @UserId
  AND deleted_at_utc IS NULL
  AND (servidor_id IS NULL OR servidor_id <> @ServidorId);",
                new { TenantId = tenantId, UserId = row.UserId.Value, ServidorId = servidorId },
                tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return servidorId;
    }

    public async Task<string> DiagnoseUserEditIdAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        const string sql = @"
WITH diagnosis AS (
    SELECT
        EXISTS(
            SELECT 1 FROM ged.servidor s
            WHERE s.id = @Id AND s.tenant_id = @TenantId
        ) AS exists_as_servidor,
        EXISTS(
            SELECT 1 FROM ged.servidor s
            WHERE s.id = @Id AND s.tenant_id = @TenantId AND COALESCE(s.reg_status, 'A') = 'A'
        ) AS exists_as_servidor_active,
        EXISTS(
            SELECT 1 FROM ged.app_user u
            WHERE u.id = @Id AND u.tenant_id = @TenantId
        ) AS exists_as_user,
        EXISTS(
            SELECT 1 FROM ged.app_user u
            WHERE u.id = @Id AND u.tenant_id = @TenantId AND u.deleted_at_utc IS NULL
        ) AS exists_as_user_active,
        EXISTS(
            SELECT 1 FROM ged.vw_user_admin_list v
            WHERE v.tenant_id = @TenantId AND v.servidor_id = @Id
        ) AS exists_in_admin_list_by_servidor_id,
        EXISTS(
            SELECT 1 FROM ged.vw_user_admin_list v
            WHERE v.tenant_id = @TenantId AND v.user_id = @Id
        ) AS exists_in_admin_list_by_user_id,
        (
            SELECT json_build_object(
                'tenantId', v.tenant_id,
                'servidorId', v.servidor_id,
                'userId', v.user_id,
                'nomeCompleto', v.nome_completo,
                'email', v.email,
                'cpf', v.cpf,
                'matricula', v.matricula,
                'servidorExists', EXISTS(
                    SELECT 1 FROM ged.servidor s
                    WHERE s.id = v.servidor_id AND s.tenant_id = v.tenant_id
                ),
                'userExists', EXISTS(
                    SELECT 1 FROM ged.app_user au
                    WHERE au.id = v.user_id AND au.tenant_id = v.tenant_id
                )
            )
            FROM ged.vw_user_admin_list v
            WHERE v.tenant_id = @TenantId
              AND (v.servidor_id = @Id OR v.user_id = @Id)
            LIMIT 1
        ) AS admin_list_row,
        (
            SELECT u.servidor_id
            FROM ged.app_user u
            WHERE u.id = @Id AND u.tenant_id = @TenantId
            LIMIT 1
        ) AS linked_servidor_from_user_id
)
SELECT json_build_object(
    'routeId', @Id,
    'tenantId', @TenantId,
    'existsAsServidor', exists_as_servidor,
    'existsAsServidorActive', exists_as_servidor_active,
    'existsAsUser', exists_as_user,
    'existsAsUserActive', exists_as_user_active,
    'existsInAdminListByServidorId', exists_in_admin_list_by_servidor_id,
    'existsInAdminListByUserId', exists_in_admin_list_by_user_id,
    'adminListRow', admin_list_row,
    'linkedServidorFromUserId', linked_servidor_from_user_id,
    'recommendation', CASE
        WHEN exists_as_user = TRUE AND exists_as_servidor = FALSE THEN
            'O ID enviado é UserId. A edição deve abrir pelo fallback UserId ou a listagem deve usar ServidorId real.'
        WHEN exists_in_admin_list_by_servidor_id = TRUE AND exists_as_servidor = FALSE THEN
            'A view lista um servidor_id inexistente em ged.servidor. Corrigir a view ou reparar dados.'
        WHEN exists_in_admin_list_by_user_id = TRUE THEN
            'A view lista esse ID como UserId. Usar fallback por UserId.'
        WHEN exists_as_servidor = FALSE AND exists_as_user = FALSE AND exists_in_admin_list_by_servidor_id = FALSE AND exists_in_admin_list_by_user_id = FALSE THEN
            'ID não existe na base para este tenant.'
        ELSE
            'ID localizado. Verificar flags de ativo e vínculo servidor/usuário para definir a melhor rota de edição.'
    END
)::text
FROM diagnosis;
";
        await using var con = await _db.OpenAsync(ct);
        return await con.ExecuteScalarAsync<string>(
            new CommandDefinition(sql, new { TenantId = tenantId, Id = id }, cancellationToken: ct)) ?? "{}";
    }

    public async Task UpdateServidorUsuarioAsync(
        Guid tenantId,
        UpdateServidorUsuarioCommand command,
        CancellationToken ct)
    {
        const string insertServidorSql = @"
INSERT INTO ged.servidor (
    id,
    tenant_id,
    nome_completo,
    cpf,
    rg,
    data_nascimento,
    email_institucional,
    email_alternativo,
    telefone,
    celular,
    matricula,
    cargo,
    funcao,
    setor,
    lotacao,
    unidade,
    tipo_vinculo,
    conselho_profissional,
    numero_conselho,
    uf_conselho,
    especialidade,
    data_admissao,
    situacao_funcional,
    observacao,
    created_by,
    created_at,
    reg_status
)
VALUES (
    @ServidorId,
    @TenantId,
    @NomeCompleto,
    @Cpf,
    @Rg,
    @DataNascimento::date,
    @EmailInstitucional,
    @EmailAlternativo,
    @Telefone,
    @Celular,
    @Matricula,
    @Cargo,
    @Funcao,
    @Setor,
    @Lotacao,
    @Unidade,
    @TipoVinculo,
    @ConselhoProfissional,
    @NumeroConselho,
    @UfConselho,
    @Especialidade,
    @DataAdmissao::date,
    @SituacaoFuncional,
    @Observacao,
    @UpdatedBy,
    now(),
    'A'
);
";

        const string updateServidorSql = @"
UPDATE ged.servidor
SET
    nome_completo = @NomeCompleto,
    cpf = @Cpf,
    rg = @Rg,
    data_nascimento = @DataNascimento::date,
    email_institucional = @EmailInstitucional,
    email_alternativo = @EmailAlternativo,
    telefone = @Telefone,
    celular = @Celular,
    matricula = @Matricula,
    cargo = @Cargo,
    funcao = @Funcao,
    setor = @Setor,
    lotacao = @Lotacao,
    unidade = @Unidade,
    tipo_vinculo = @TipoVinculo,
    conselho_profissional = @ConselhoProfissional,
    numero_conselho = @NumeroConselho,
    uf_conselho = @UfConselho,
    especialidade = @Especialidade,
    data_admissao = @DataAdmissao::date,
    situacao_funcional = @SituacaoFuncional,
    observacao = @Observacao,
    updated_by = @UpdatedBy,
    updated_at = now()
WHERE tenant_id = @TenantId
  AND id = @ServidorId
  AND reg_status = 'A';
";

        const string updateUserSql = @"
UPDATE ged.app_user
SET
    servidor_id = @ServidorId,
    name = @NomeCompleto,
    email = @EmailLogin,
    normalized_email = upper(@EmailLogin),
    user_name = @UserName,
    normalized_user_name = upper(@UserName),
    phone_number = @PhoneNumber,
    is_active = @IsActive,
    must_change_password = @MustChangePassword,
    mfa_enabled = @MfaEnabled,
    certificate_required = @CertificateRequired,
    can_sign_with_icp = @CanSignWithIcp,
    security_level = @SecurityLevel::ged.security_level,
    updated_at_utc = now()
WHERE tenant_id = @TenantId
  AND id = @UserId
  AND deleted_at_utc IS NULL;
";
        const string insertUserSql = @"
INSERT INTO ged.app_user (
    id,
    tenant_id,
    servidor_id,
    name,
    email,
    normalized_email,
    user_name,
    normalized_user_name,
    phone_number,
    is_active,
    must_change_password,
    mfa_enabled,
    certificate_required,
    can_sign_with_icp,
    security_level,
    created_at
)
VALUES (
    @UserId,
    @TenantId,
    @ServidorId,
    @NomeCompleto,
    @EmailLogin,
    upper(@EmailLogin),
    @UserName,
    upper(@UserName),
    @PhoneNumber,
    @IsActive,
    @MustChangePassword,
    @MfaEnabled,
    @CertificateRequired,
    @CanSignWithIcp,
    @SecurityLevel::ged.security_level,
    now()
);
";

        const string deleteRolesSql = @"
DELETE FROM ged.user_role
WHERE user_id = @UserId;
";

        const string insertRoleSql = @"
INSERT INTO ged.user_role (user_id, role_id)
VALUES (@UserId, @RoleId)
ON CONFLICT DO NOTHING;
";

        const string auditSql = @"
SELECT ged.audit_user_security_event(
    @TenantId,
    @UserId,
    @ServidorId,
    @EventType,
    @EventDescription,
    @UpdatedBy,
    @IpAddress,
    @UserAgent,
    @CorrelationId,
    @Data::jsonb
);
";

        await using var con = await _db.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

        var servidorId = command.ServidorId != Guid.Empty
            ? command.ServidorId
            : Guid.NewGuid();

        var emailLogin = TrimLowerOrNull(command.EmailLogin) ?? "";
        var userName = TrimOrNull(command.UserName) ?? emailLogin;

        var servidorParams = new
        {
            TenantId = tenantId,
            ServidorId = servidorId,

            NomeCompleto = command.NomeCompleto.Trim(),
            Cpf = NormalizeCpf(command.Cpf),
            Rg = TrimOrNull(command.Rg),
            DataNascimento = command.DataNascimento?.Date,

            EmailInstitucional = TrimLowerOrNull(command.EmailInstitucional),
            EmailAlternativo = TrimLowerOrNull(command.EmailAlternativo),
            Telefone = TrimOrNull(command.Telefone),
            Celular = TrimOrNull(command.Celular),

            Matricula = TrimOrNull(command.Matricula),
            Cargo = TrimOrNull(command.Cargo),
            Funcao = TrimOrNull(command.Funcao),
            Setor = TrimOrNull(command.Setor),
            Lotacao = TrimOrNull(command.Lotacao),
            Unidade = TrimOrNull(command.Unidade),
            TipoVinculo = TrimOrNull(command.TipoVinculo),

            ConselhoProfissional = TrimOrNull(command.ConselhoProfissional),
            NumeroConselho = TrimOrNull(command.NumeroConselho),
            UfConselho = TrimUpperOrNull(command.UfConselho),
            Especialidade = TrimOrNull(command.Especialidade),

            DataAdmissao = command.DataAdmissao?.Date,
            SituacaoFuncional = string.IsNullOrWhiteSpace(command.SituacaoFuncional)
                ? "ATIVO"
                : command.SituacaoFuncional.Trim().ToUpperInvariant(),

            Observacao = TrimOrNull(command.Observacao),
            command.UpdatedBy
        };

        var isServidorNovo = command.ServidorId == Guid.Empty;

        if (isServidorNovo)
        {
            await con.ExecuteAsync(
                new CommandDefinition(
                    insertServidorSql,
                    servidorParams,
                    transaction: tx,
                    cancellationToken: ct));
        }
        else
        {
            var linhasAtualizadas = await con.ExecuteAsync(
                new CommandDefinition(
                    updateServidorSql,
                    servidorParams,
                    transaction: tx,
                    cancellationToken: ct));

            if (linhasAtualizadas == 0)
            {
                await con.ExecuteAsync(
                    new CommandDefinition(
                        insertServidorSql,
                        servidorParams,
                        transaction: tx,
                        cancellationToken: ct));
            }
        }

        if (command.UserId.HasValue || command.CriarUsuarioAcesso)
        {
            var hasAccess = command.UserId.HasValue && command.UserId.Value != Guid.Empty;
            if (!hasAccess && command.CriarUsuarioAcesso)
            {
                command.UserId = Guid.NewGuid();
                hasAccess = true;
                await con.ExecuteAsync(
                    new CommandDefinition(
                        insertUserSql,
                        new
                        {
                            TenantId = tenantId,
                            UserId = command.UserId!.Value,
                            ServidorId = servidorId,
                            NomeCompleto = command.NomeCompleto.Trim(),
                            EmailLogin = emailLogin,
                            UserName = userName,
                            PhoneNumber = TrimOrNull(command.Celular) ?? TrimOrNull(command.Telefone),
                            command.IsActive,
                            command.MustChangePassword,
                            command.MfaEnabled,
                            command.CertificateRequired,
                            command.CanSignWithIcp,
                            SecurityLevel = NormalizeSecurityLevel(command.SecurityLevel)
                        },
                        transaction: tx,
                        cancellationToken: ct));
            }
            else if (hasAccess)
            {
                await con.ExecuteAsync(
                    new CommandDefinition(
                        updateUserSql,
                        new
                        {
                            TenantId = tenantId,
                            UserId = command.UserId!.Value,
                            ServidorId = servidorId,
                            NomeCompleto = command.NomeCompleto.Trim(),
                            EmailLogin = emailLogin,
                            UserName = userName,
                            PhoneNumber = TrimOrNull(command.Celular) ?? TrimOrNull(command.Telefone),
                            command.IsActive,
                            command.MustChangePassword,
                            command.MfaEnabled,
                            command.CertificateRequired,
                            command.CanSignWithIcp,
                            SecurityLevel = NormalizeSecurityLevel(command.SecurityLevel)
                        },
                        transaction: tx,
                        cancellationToken: ct));
            }

            await con.ExecuteAsync(
                new CommandDefinition(
                    deleteRolesSql,
                    new { UserId = command.UserId!.Value },
                    transaction: tx,
                    cancellationToken: ct));

            foreach (var roleId in command.RoleIds.Where(x => x != Guid.Empty).Distinct())
            {
                await con.ExecuteAsync(
                    new CommandDefinition(
                        insertRoleSql,
                        new
                        {
                            UserId = command.UserId!.Value,
                            RoleId = roleId
                        },
                        transaction: tx,
                        cancellationToken: ct));
            }

            await con.ExecuteAsync(
                new CommandDefinition(
                    auditSql,
                    new
                    {
                        TenantId = tenantId,
                        UserId = command.UserId,
                        ServidorId = servidorId,
                        EventType = isServidorNovo ? "USER_UPDATE_WITH_SERVER_CREATE" : "USER_UPDATE",
                        EventDescription = isServidorNovo
                            ? "Cadastro de usuário antigo atualizado e servidor vinculado criado."
                            : "Cadastro de servidor/usuário atualizado.",
                        command.UpdatedBy,
                        command.IpAddress,
                        command.UserAgent,
                        command.CorrelationId,
                        Data = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            EntityId = servidorId,
                            command.UserId,
                            command.NomeCompleto,
                            Cpf = NormalizeCpf(command.Cpf),
                            command.Matricula,
                            command.Setor,
                            command.Cargo,
                            command.Funcao,
                            command.SecurityLevel,
                            Roles = command.RoleIds,
                            ServidorCriado = isServidorNovo
                        })
                    },
                    transaction: tx,
                    cancellationToken: ct));
        }
        else
        {
            await con.ExecuteAsync(
                new CommandDefinition(
                    auditSql,
                    new
                    {
                        TenantId = tenantId,
                        UserId = (Guid?)null,
                        ServidorId = servidorId,
                        EventType = "SERVER_UPDATE",
                        EventDescription = "Cadastro institucional do servidor atualizado sem usuário de acesso.",
                        command.UpdatedBy,
                        command.IpAddress,
                        command.UserAgent,
                        command.CorrelationId,
                        Data = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            EntityId = servidorId,
                            command.UserId,
                            command.NomeCompleto,
                            Cpf = NormalizeCpf(command.Cpf),
                            command.Matricula,
                            command.Setor,
                            command.Cargo,
                            command.Funcao
                        })
                    },
                    transaction: tx,
                    cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string TechnicalCpf(Guid servidorId)
    {
        return "SEMCPF" + servidorId.ToString("N")[..8];
    }

    private sealed class AppUserRepairRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? ServidorId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class AdminListRepairRow
    {
        public Guid? ServidorId { get; set; }
        public Guid? UserId { get; set; }
        public string? NomeCompleto { get; set; }
        public string? Email { get; set; }
        public string? Cpf { get; set; }
        public string? Matricula { get; set; }
        public string? Cargo { get; set; }
        public string? Funcao { get; set; }
        public string? Setor { get; set; }
        public string? Lotacao { get; set; }
        public string? Unidade { get; set; }
    }
}
