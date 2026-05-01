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
    @DataNascimento,
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
    @DataAdmissao,
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
    data_nascimento = @DataNascimento,
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
    data_admissao = @DataAdmissao,
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
                DataNascimento = command.DataNascimento,
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
                DataAdmissao = command.DataAdmissao,
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
        string cpf,
        Guid? ignoreServidorId,
        CancellationToken ct)
    {
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
                    Cpf = cpf,
                    IgnoreServidorId = ignoreServidorId
                },
                cancellationToken: ct));
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
}