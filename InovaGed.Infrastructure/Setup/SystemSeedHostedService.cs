using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Security;
using InovaGed.Application.SystemHealth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.Setup;

public sealed class SystemSeedOptions
{
    public bool Enabled { get; set; } = false;
    public bool AllowInPoc { get; set; }
    public bool FailFastOnSeedError { get; set; }
    public bool UpdateExistingPasswords { get; set; }
    public bool NormalizeLegacyRoles { get; set; } = true;
}

public sealed class SystemSeedHostedService : IHostedService
{
    private static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SystemSeedHostedService> _logger;
    private readonly ISchemaCompatibilityState _schemaState;
    private readonly SystemSeedOptions _options;
    private readonly IHostEnvironment _environment;
    private NpgsqlTransaction? _currentSeedTransaction;

    private NpgsqlTransaction CurrentSeedTransaction => _currentSeedTransaction ?? throw new InvalidOperationException("System Seed transaction was not initialized.");

    public SystemSeedHostedService(
        IDbConnectionFactory db,
        ILogger<SystemSeedHostedService> logger,
        ISchemaCompatibilityState schemaState,
        IOptions<SystemSeedOptions> options,
        IHostEnvironment environment)
    {
        _db = db;
        _logger = logger;
        _schemaState = schemaState;
        _options = options.Value;
        _environment = environment;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("SystemSeedHostedService desabilitado por configuração.");
            return;
        }

        if (!_environment.IsDevelopment() && !_options.AllowInPoc)
        {
            _logger.LogCritical("SystemSeed bloqueado fora de Development porque AllowInPoc=false. Environment={Environment}", _environment.EnvironmentName);
            return;
        }

        if (_environment.IsProduction())
        {
            _logger.LogCritical("SystemSeed bloqueado em Production para impedir criação de usuários demonstrativos.");
            return;
        }

        if (!await _schemaState.IsCompatibleAsync("SystemSeed", ct))
        {
            _logger.LogWarning("SystemSeedHostedService não iniciado: schema incompatível. Execute migrations.");
            return;
        }

        try
        {
            await RunSeedAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no System Seed.");

            if (_options.FailFastOnSeedError)
                throw;

            _logger.LogWarning("System Seed falhou, mas a aplicação continuará porque FailFastOnSeedError=false.");
        }
    }

    private async Task RunSeedAsync(CancellationToken ct)
    {
        _logger.LogInformation("System Seed START");

        await using var conn = await _db.OpenAsync(ct);

        if (!await HasRequiredIdentitySchemaAsync(conn, ct))
        {
            _logger.LogWarning("SystemSeed não executado porque schema de usuários ainda não está pronto.");
            return;
        }

        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            _currentSeedTransaction = tx;
            await EnsureSeedColumnsAsync(conn, CurrentSeedTransaction, ct);

            var hasher = new PasswordHasher<ApplicationUser>();
            var seedUsers = BuildSeedUsers(hasher);
            var roleIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var roleName in new[] { "ADMIN", "ADMINISTRADOR", "ADMINISTRADOROPHIR", "ARQUIVISTAOPHIR", "HOSPITAL" })
                roleIds[roleName] = await GetOrCreateRoleAsync(conn, DefaultTenantId, roleName, ct);

            foreach (var seedUser in seedUsers)
            {
                var userId = await GetOrCreateUserAsync(
                    conn,
                    DefaultTenantId,
                    seedUser.Id,
                    seedUser.Name,
                    seedUser.Email,
                    seedUser.PasswordHash,
                    ct);

                await EnsureUserRoleAsync(conn, DefaultTenantId, userId, roleIds[seedUser.RoleName], ct);
            }

            if (_options.NormalizeLegacyRoles)
                await NormalizeLegacyRolesAsync(conn, CurrentSeedTransaction, DefaultTenantId, roleIds, ct);

            await tx.CommitAsync(ct);
            _logger.LogInformation("System Seed FINISH");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            _currentSeedTransaction = null;
        }
    }

    private async Task<Guid> GetOrCreateUserAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        Guid desiredUserId,
        string name,
        string email,
        string passwordHash,
        CancellationToken ct)
    {
        var normalizedEmail = NormalizeEmail(email);
        var normalizedLogin = NormalizeLogin(normalizedEmail);

        var existingById = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            select id
            from ged.app_user
            where id = @DesiredUserId
            limit 1
            """,
            new { DesiredUserId = desiredUserId },
            CurrentSeedTransaction,
            cancellationToken: ct));

        if (existingById.HasValue)
        {
            var conflictingEmailUserId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                """
                select id
                from ged.app_user
                where tenant_id = @TenantId
                  and lower(trim(email)) = lower(@Email)
                  and id <> @Id
                limit 1
                """,
                new { TenantId = tenantId, Email = normalizedEmail, Id = desiredUserId },
                CurrentSeedTransaction,
                cancellationToken: ct));

            if (conflictingEmailUserId.HasValue)
            {
                _logger.LogWarning(
                    "Seed encontrou e-mail já usado por outro usuário. Mantendo e-mail atual do usuário Id={UserId}. Email={Email} OutroUsuarioId={OtherUserId}",
                    desiredUserId,
                    normalizedEmail,
                    conflictingEmailUserId.Value);

                await UpdateExistingUserWithoutLoginAsync(conn, CurrentSeedTransaction, desiredUserId, name, passwordHash, ct);
            }
            else
            {
                await UpdateExistingUserAsync(conn, CurrentSeedTransaction, desiredUserId, name, normalizedEmail, normalizedLogin, passwordHash, ct);
            }

            _logger.LogInformation("Seed usuário já existe por ID. UserId={UserId}", desiredUserId);
            return existingById.Value;
        }

        var existingByEmail = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            select id
            from ged.app_user
            where tenant_id = @TenantId
              and lower(trim(email)) = lower(@Email)
            limit 1
            """,
            new { TenantId = tenantId, Email = normalizedEmail },
            CurrentSeedTransaction,
            cancellationToken: ct));

        if (existingByEmail.HasValue)
        {
            await UpdateExistingUserAsync(conn, CurrentSeedTransaction, existingByEmail.Value, name, normalizedEmail, normalizedLogin, passwordHash, ct);
            _logger.LogInformation("Seed usuário já existe por e-mail. Email={Email} UserId={UserId}", normalizedEmail, existingByEmail.Value);
            return existingByEmail.Value;
        }

        var existingByLogin = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            select id
            from ged.app_user
            where tenant_id = @TenantId
              and (
                    lower(trim(user_name)) = lower(@Login)
                 or normalized_user_name = @NormalizedLogin
              )
            limit 1
            """,
            new { TenantId = tenantId, Login = normalizedEmail, NormalizedLogin = normalizedLogin },
            CurrentSeedTransaction,
            cancellationToken: ct));

        if (existingByLogin.HasValue)
        {
            await UpdateExistingUserAsync(conn, CurrentSeedTransaction, existingByLogin.Value, name, normalizedEmail, normalizedLogin, passwordHash, ct);
            _logger.LogInformation("Seed usuário já existe por login. Login={Login} UserId={UserId}", normalizedEmail, existingByLogin.Value);
            return existingByLogin.Value;
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into ged.app_user (
                id,
                tenant_id,
                name,
                email,
                normalized_email,
                user_name,
                normalized_user_name,
                password_hash,
                is_active,
                must_change_password,
                created_at
            )
            values (
                @Id,
                @TenantId,
                @Name,
                @Email,
                @NormalizedEmail,
                @UserName,
                @NormalizedUserName,
                @PasswordHash,
                true,
                false,
                now()
            )
            """,
            new
            {
                Id = desiredUserId,
                TenantId = tenantId,
                Name = name,
                Email = normalizedEmail,
                NormalizedEmail = normalizedLogin,
                UserName = normalizedEmail,
                NormalizedUserName = normalizedLogin,
                PasswordHash = passwordHash
            },
            CurrentSeedTransaction,
            cancellationToken: ct));

        _logger.LogInformation("Seed usuário criado. Email={Email} UserId={UserId}", normalizedEmail, desiredUserId);
        return desiredUserId;
    }

    private async Task<Guid> GetOrCreateRoleAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        string roleName,
        CancellationToken ct)
    {
        var normalizedName = NormalizeRole(roleName);

        var existingRoleId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            select id
            from ged.app_role
            where tenant_id = @TenantId
              and normalized_name = @NormalizedName
            limit 1
            """,
            new { TenantId = tenantId, NormalizedName = normalizedName },
            CurrentSeedTransaction,
            cancellationToken: ct));

        if (existingRoleId.HasValue)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                update ged.app_role
                set name = @Name
                where id = @Id
                  and name <> @Name
                """,
                new { Id = existingRoleId.Value, Name = normalizedName },
                CurrentSeedTransaction,
                cancellationToken: ct));

            _logger.LogInformation("Seed role já existe. Role={Role}", normalizedName);
            return existingRoleId.Value;
        }

        var roleId = Guid.NewGuid();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into ged.app_role (id, tenant_id, name, normalized_name, created_at)
            values (@Id, @TenantId, @Name, @NormalizedName, now())
            on conflict do nothing
            """,
            new { Id = roleId, TenantId = tenantId, Name = normalizedName, NormalizedName = normalizedName },
            CurrentSeedTransaction,
            cancellationToken: ct));

        var createdOrConcurrentRoleId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            select id
            from ged.app_role
            where tenant_id = @TenantId
              and normalized_name = @NormalizedName
            limit 1
            """,
            new { TenantId = tenantId, NormalizedName = normalizedName },
            CurrentSeedTransaction,
            cancellationToken: ct));

        _logger.LogInformation("Seed role criada. Role={Role} RoleId={RoleId}", normalizedName, createdOrConcurrentRoleId);
        return createdOrConcurrentRoleId;
    }

    private async Task EnsureUserRoleAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        Guid userId,
        Guid roleId,
        CancellationToken ct)
    {
        var roleName = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            select normalized_name
            from ged.app_role
            where tenant_id = @TenantId
              and id = @RoleId
            limit 1
            """,
            new { TenantId = tenantId, RoleId = roleId },
            CurrentSeedTransaction,
            cancellationToken: ct)) ?? roleId.ToString();
        var exists = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            select 1
            from ged.user_role
            where user_id = @UserId
              and role_id = @RoleId
            limit 1
            """,
            new { UserId = userId, RoleId = roleId },
            CurrentSeedTransaction,
            cancellationToken: ct));

        if (exists.HasValue)
        {
            _logger.LogInformation("Seed vínculo user-role já existe. UserId={UserId} Role={Role}", userId, NormalizeRole(roleName));
            return;
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into ged.user_role (user_id, role_id)
            values (@UserId, @RoleId)
            on conflict do nothing
            """,
            new { TenantId = tenantId, UserId = userId, RoleId = roleId },
            CurrentSeedTransaction,
            cancellationToken: ct));

        _logger.LogInformation("Seed vínculo user-role criado. UserId={UserId} Role={Role}", userId, NormalizeRole(roleName));
    }

    private async Task UpdateExistingUserAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid userId,
        string name,
        string normalizedEmail,
        string normalizedLogin,
        string passwordHash,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            """
            update ged.app_user
            set name = @Name,
                email = @Email,
                normalized_email = @NormalizedEmail,
                user_name = case
                    when not exists (
                        select 1
                        from ged.app_user other
                        where other.tenant_id = ged.app_user.tenant_id
                          and other.id <> ged.app_user.id
                          and (
                                lower(trim(other.user_name)) = lower(@UserName)
                             or other.normalized_user_name = @NormalizedUserName
                          )
                    ) then @UserName
                    else user_name
                end,
                normalized_user_name = case
                    when not exists (
                        select 1
                        from ged.app_user other
                        where other.tenant_id = ged.app_user.tenant_id
                          and other.id <> ged.app_user.id
                          and (
                                lower(trim(other.user_name)) = lower(@UserName)
                             or other.normalized_user_name = @NormalizedUserName
                          )
                    ) then @NormalizedUserName
                    else normalized_user_name
                end,
                is_active = true,
                must_change_password = false,
                password_hash = case
                    when @UpdateExistingPasswords then @PasswordHash
                    when password_hash is null or password_hash = '' then @PasswordHash
                    else password_hash
                end,
                updated_at = now()
            where id = @Id
            """,
            new
            {
                Id = userId,
                Name = name,
                Email = normalizedEmail,
                NormalizedEmail = normalizedLogin,
                UserName = normalizedEmail,
                NormalizedUserName = normalizedLogin,
                PasswordHash = passwordHash,
                _options.UpdateExistingPasswords
            },
            tx,
            cancellationToken: ct));
    }

    private async Task UpdateExistingUserWithoutLoginAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid userId,
        string name,
        string passwordHash,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            """
            update ged.app_user
            set name = @Name,
                is_active = true,
                must_change_password = false,
                password_hash = case
                    when @UpdateExistingPasswords then @PasswordHash
                    when password_hash is null or password_hash = '' then @PasswordHash
                    else password_hash
                end,
                updated_at = now()
            where id = @Id
            """,
            new
            {
                Id = userId,
                Name = name,
                PasswordHash = passwordHash,
                _options.UpdateExistingPasswords
            },
            tx,
            cancellationToken: ct));
    }

    private async Task NormalizeLegacyRolesAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid tenantId,
        IReadOnlyDictionary<string, Guid> roleIds,
        CancellationToken ct)
    {
        var mappings = new[]
        {
            new { CanonicalRole = "ADMIN", Aliases = new[] { "ADMIN", "ADMINISTRADOR DO SISTEMA" } },
            new { CanonicalRole = "ADMINISTRADOR", Aliases = new[] { "ADMINISTRADOR", "ADMINISTRATOR" } }
        };

        foreach (var mapping in mappings)
        {
            foreach (var alias in mapping.Aliases)
            {
                var canonicalRoleId = roleIds[mapping.CanonicalRole];
                var affected = await conn.ExecuteAsync(new CommandDefinition(
                    """
                    insert into ged.user_role (user_id, role_id)
                    select distinct ur.user_id, @CanonicalRoleId
                    from ged.user_role ur
                    join ged.app_role old_role on old_role.id = ur.role_id
                    join ged.app_user u on u.id = ur.user_id and u.tenant_id = old_role.tenant_id
                    where old_role.tenant_id = @TenantId
                      and upper(trim(coalesce(old_role.normalized_name, old_role.name))) = @Alias
                      and not exists (
                          select 1
                          from ged.user_role existing
                          where existing.user_id = ur.user_id
                            and existing.role_id = @CanonicalRoleId
                      )
                    on conflict do nothing
                    """,
                    new { TenantId = tenantId, CanonicalRoleId = canonicalRoleId, Alias = alias },
                    tx,
                    cancellationToken: ct));

                if (affected > 0)
                {
                    _logger.LogInformation(
                        "Seed normalizou roles legadas. Alias={Alias} Role={Role} VinculosCriados={Count}",
                        alias,
                        mapping.CanonicalRole,
                        affected);
                }
            }
        }
    }

    private async Task EnsureSeedColumnsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        const string sql = """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;

        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS name text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS email text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS normalized_email text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS user_name text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS normalized_user_name text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS password_hash text NULL;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT false;
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
        ALTER TABLE ged.app_user ADD COLUMN IF NOT EXISTS updated_at timestamptz NULL;

        ALTER TABLE ged.app_role ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
        """;

        await conn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: ct));
    }

    private static IReadOnlyList<SeedUser> BuildSeedUsers(PasswordHasher<ApplicationUser> hasher)
    {
        var password = Environment.GetEnvironmentVariable("INOVAGED_DEV_SEED_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            return [];

        return
        [
            CreateSeedUser(hasher, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001"), "Administrador do Sistema", "admin@inovaged.local", password, "ADMIN"),
            CreateSeedUser(hasher, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb002"), "Administrador", "administrador@inovaged.local", password, "ADMINISTRADOR"),
            CreateSeedUser(hasher, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb003"), "Administrador Ophir", "administradorophir@inovaged.local", password, "ADMINISTRADOROPHIR"),
            CreateSeedUser(hasher, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb004"), "Arquivista Ophir", "arquivistaophir@inovaged.local", password, "ARQUIVISTAOPHIR"),
            CreateSeedUser(hasher, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb005"), "Hospital", "hospital@inovaged.local", password, "HOSPITAL")
        ];
    }

    private static SeedUser CreateSeedUser(PasswordHasher<ApplicationUser> hasher, Guid id, string name, string email, string password, string roleName)
    {
        var normalizedEmail = NormalizeEmail(email);
        var passwordHash = hasher.HashPassword(new ApplicationUser { Id = id, TenantId = DefaultTenantId, Email = normalizedEmail }, password);
        return new SeedUser(id, name, normalizedEmail, passwordHash, roleName);
    }

    private async Task<bool> HasRequiredIdentitySchemaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var missingObjects = await conn.QueryAsync<string>(new CommandDefinition(
            """
            with required_objects(schema_name, object_name, object_type) as (
                values
                    ('ged', 'app_user', 'BASE TABLE'),
                    ('ged', 'app_role', 'BASE TABLE'),
                    ('ged', 'user_role', 'BASE TABLE')
            )
            select schema_name || '.' || object_name
            from required_objects ro
            where not exists (
                select 1
                from information_schema.tables t
                where t.table_schema = ro.schema_name
                  and t.table_name = ro.object_name
                  and t.table_type = ro.object_type
            )
            """,
            cancellationToken: ct));

        var missingObjectList = missingObjects.ToList();
        if (missingObjectList.Count > 0)
        {
            _logger.LogWarning("SystemSeed schema incompleto. Tabelas ausentes: {MissingObjects}", string.Join(", ", missingObjectList));
            return false;
        }

        var missingColumns = await conn.QueryAsync<string>(new CommandDefinition(
            """
            with required_columns(table_name, column_name) as (
                values
                    ('app_user', 'id'),
                    ('app_user', 'tenant_id'),
                    ('app_role', 'id'),
                    ('app_role', 'tenant_id'),
                    ('app_role', 'name'),
                    ('app_role', 'normalized_name'),
                    ('user_role', 'user_id'),
                    ('user_role', 'role_id')
            )
            select 'ged.' || rc.table_name || '.' || rc.column_name
            from required_columns rc
            where not exists (
                select 1
                from information_schema.columns c
                where c.table_schema = 'ged'
                  and c.table_name = rc.table_name
                  and c.column_name = rc.column_name
            )
            """,
            cancellationToken: ct));

        var missingColumnList = missingColumns.ToList();
        if (missingColumnList.Count > 0)
        {
            _logger.LogWarning("SystemSeed schema incompleto. Colunas ausentes: {MissingColumns}", string.Join(", ", missingColumnList));
            return false;
        }

        return true;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static string NormalizeLogin(string login) => login.Trim().ToUpperInvariant();
    private static string NormalizeRole(string roleName) => roleName.Trim().ToUpperInvariant();

    private sealed record SeedUser(Guid Id, string Name, string Email, string PasswordHash, string RoleName);
}
