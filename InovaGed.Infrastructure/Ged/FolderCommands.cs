using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged;

public sealed class FolderCommands : IFolderCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<FolderCommands> _logger;

    public FolderCommands(IDbConnectionFactory db, ILogger<FolderCommands> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<Guid>> CreateAsync(
        Guid tenantId,
        string name,
        Guid? parentId,
        Guid? departmentId,
        Guid? createdBy,
        CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (string.IsNullOrWhiteSpace(name)) return Result<Guid>.Fail("VALIDATION", "Nome da pasta é obrigatório.");

            var id = Guid.NewGuid();

            // ✅ Tabela real no seu bdged.sql: ged.folder
            const string sql = @"
INSERT INTO ged.folder
(id, tenant_id, name, parent_id, department_id, is_active, created_at, created_by)
VALUES
(@id, @tenantId, @name, @parentId, @departmentId, TRUE, NOW(), @createdBy);";

            using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                id,
                tenantId,
                name = name.Trim(),
                parentId,
                departmentId,
                createdBy
            }, cancellationToken: ct));

            return Result<Guid>.Ok(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar pasta.");
            return Result<Guid>.Fail("ERR", "Erro ao criar pasta.");
        }
    }

    public async Task<Result> RenameAsync(Guid tenantId, Guid folderId, string newName, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (folderId == Guid.Empty) return Result.Fail("VALIDATION", "Pasta inválida.");
            if (string.IsNullOrWhiteSpace(newName)) return Result.Fail("VALIDATION", "Nome da pasta é obrigatório.");

            const string sql = @"
UPDATE ged.folder
SET name = @newName
WHERE tenant_id = @tenantId AND id = @folderId;";

            using var conn = await _db.OpenAsync(ct);

            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenantId,
                folderId,
                newName = newName.Trim()
            }, cancellationToken: ct));

            return rows > 0
                ? Result.Ok()
                : Result.Fail("NOT_FOUND", "Pasta não encontrada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao renomear pasta.");
            return Result.Fail("ERR", "Erro ao renomear pasta.");
        }
    }

    public async Task<Result> DeactivateAsync(Guid tenantId, Guid folderId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (folderId == Guid.Empty) return Result.Fail("VALIDATION", "Pasta inválida.");

            const string sql = @"
UPDATE ged.folder
SET is_active = FALSE
WHERE tenant_id = @tenantId AND id = @folderId;";

            using var conn = await _db.OpenAsync(ct);

            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));

            return rows > 0
                ? Result.Ok()
                : Result.Fail("NOT_FOUND", "Pasta não encontrada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao desativar pasta.");
            return Result.Fail("ERR", "Erro ao desativar pasta.");
        }
    }
}
