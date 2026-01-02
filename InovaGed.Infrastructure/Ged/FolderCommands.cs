using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged;

public sealed class FolderCommands : IFolderCommands
{
    private static readonly Regex SafeIdent =
        new(@"^[a-z_][a-z0-9_]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            // ✅ Tabela real no seu bd: ged.folder
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

    public async Task<Result> RenameAsync(
        Guid tenantId,
        Guid folderId,
        string newName,
        CancellationToken ct)
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

    public async Task DeactivateAsync(Guid tenantId, Guid folderId, Guid? userId, CancellationToken ct)
    {
        using var con = await _db.OpenAsync(ct);
        using var tx = con.BeginTransaction();

        try
        {
            var modeCol = await PickFolderSoftDeleteModeAsync(con, tx, ct);
            if (modeCol is null)
                throw new InvalidOperationException("Não encontrei coluna de status em ged.folder (is_active/active/reg_status).");

            string sql = modeCol switch
            {
                "is_active" => @"
UPDATE ged.folder
SET is_active = FALSE
WHERE tenant_id=@tenantId AND id=@folderId;",
                "active" => @"
UPDATE ged.folder
SET active = FALSE
WHERE tenant_id=@tenantId AND id=@folderId;",
                "reg_status" => @"
UPDATE ged.folder
SET reg_status = 'D'
WHERE tenant_id=@tenantId AND id=@folderId;",
                "deleted_at_utc" => @"
UPDATE ged.folder
SET deleted_at_utc = NOW()
WHERE tenant_id=@tenantId AND id=@folderId;",
                "deleted_at" => @"
UPDATE ged.folder
SET deleted_at = NOW()
WHERE tenant_id=@tenantId AND id=@folderId;",
                _ => throw new InvalidOperationException($"Modo não suportado: {modeCol}")
            };

            _logger.LogInformation("DeactivateAsync folder usando coluna={Col}", modeCol);

            await con.ExecuteAsync(new CommandDefinition(
                sql, new { tenantId, folderId }, tx, cancellationToken: ct));

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<Result> DeleteRecursiveAsync(Guid tenantId, Guid folderId, Guid? userId, CancellationToken ct)
    {
        _logger.LogInformation(">>> DeleteRecursiveAsync START | Tenant={Tenant} Folder={Folder} User={User}",
            tenantId, folderId, userId);

        using var con = await _db.OpenAsync(ct);
        using var tx = con.BeginTransaction();

        try
        {
            // 1) subárvore
            const string sqlTree = @"
WITH RECURSIVE t AS (
    SELECT id
    FROM ged.folder
    WHERE tenant_id = @tenantId
      AND id = @folderId

    UNION ALL

    SELECT f.id
    FROM ged.folder f
    JOIN t ON t.id = f.parent_id
    WHERE f.tenant_id = @tenantId
)
SELECT id FROM t;";

            var folderIds = (await con.QueryAsync<Guid>(
                new CommandDefinition(sqlTree, new { tenantId, folderId }, tx, cancellationToken: ct)
            )).AsList();

            _logger.LogInformation("Subárvore de pastas encontrada: {Count}", folderIds.Count);

            if (folderIds.Count == 0)
            {
                tx.Commit();
                return Result.Ok();
            }

            // ✅ enum real: ged.document_status_enum = DRAFT | ACTIVE | ARCHIVED
            // Então o "deletado" precisa ser ARCHIVED
            var deletedStatus = "ARCHIVED";

            // 2) INATIVAR DOCUMENTOS (cast para enum)
            const string sqlDocs = @"
UPDATE ged.document
SET status = @deletedStatus::ged.document_status_enum
WHERE tenant_id = @tenantId
  AND folder_id = ANY(@folderIds);";

            var docs = await con.ExecuteAsync(new CommandDefinition(
                sqlDocs,
                new { tenantId, folderIds = folderIds.ToArray(), deletedStatus },
                tx,
                cancellationToken: ct));

            _logger.LogInformation("Docs afetados: {Count}", docs);

            // 3) Se existir status em document_version, atualiza também (enum vs texto)
            var verUdtName = await con.ExecuteScalarAsync<string?>(new CommandDefinition(@"
SELECT c.udt_name
FROM information_schema.columns c
WHERE c.table_schema='ged'
  AND c.table_name='document_version'
  AND c.column_name='status'
LIMIT 1;", transaction: tx, cancellationToken: ct));

            if (!string.IsNullOrWhiteSpace(verUdtName))
            {
                // Se for enum, faz cast. Se for texto, grava direto.
                var setExpr = verUdtName.Equals("document_status_enum", StringComparison.OrdinalIgnoreCase)
                    ? "status = @deletedStatus::ged.document_status_enum"
                    : "status = @deletedStatus";

                var sqlVers = $@"
UPDATE ged.document_version
SET {setExpr}
WHERE tenant_id = @tenantId
  AND document_id IN (
      SELECT id FROM ged.document
      WHERE tenant_id=@tenantId
        AND folder_id = ANY(@folderIds)
  );";

                var vers = await con.ExecuteAsync(new CommandDefinition(
                    sqlVers,
                    new { tenantId, folderIds = folderIds.ToArray(), deletedStatus },
                    tx,
                    cancellationToken: ct));

                _logger.LogInformation("Versões afetadas: {Count}", vers);
            }
            else
            {
                _logger.LogWarning("ged.document_version não possui coluna status. Pulando update de versões.");
            }

            // 4) INATIVAR PASTAS
            const string sqlFolders = @"
UPDATE ged.folder
SET is_active = FALSE
WHERE tenant_id = @tenantId
  AND id = ANY(@folderIds);";

            var folders = await con.ExecuteAsync(new CommandDefinition(
                sqlFolders, new { tenantId, folderIds = folderIds.ToArray() }, tx, cancellationToken: ct));

            _logger.LogInformation("Pastas inativadas: {Count}", folders);

            tx.Commit();
            _logger.LogInformation(">>> DeleteRecursiveAsync SUCCESS");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, ">>> DeleteRecursiveAsync ERROR");
            return Result.Fail("FOLDER_DELETE_ERROR", "Erro ao excluir pasta.");
        }
    }

    // ===== helpers =====
    private static async Task<string?> PickExistingTableAsync(IDbConnection con, IDbTransaction tx,
        string schema, params string[] tables)
    {
        foreach (var t in tables)
        {
            if (!SafeIdent.IsMatch(t)) continue;

            var n = await con.ExecuteScalarAsync<int>(
                @"SELECT COUNT(1)
                  FROM information_schema.tables
                  WHERE table_schema=@schema AND table_name=@t;",
                new { schema, t }, tx);

            if (n > 0) return t;
        }
        return null;
    }

    private static async Task<string?> PickExistingColumnAsync(IDbConnection con, IDbTransaction tx,
        string schema, string table, params string[] cols)
    {
        foreach (var c in cols)
        {
            if (!SafeIdent.IsMatch(c)) continue;

            var n = await con.ExecuteScalarAsync<int>(
                @"SELECT COUNT(1)
                  FROM information_schema.columns
                  WHERE table_schema=@schema AND table_name=@table AND column_name=@c;",
                new { schema, table, c }, tx);

            if (n > 0) return c;
        }
        return null;
    }

    private async Task<string?> PickFolderSoftDeleteModeAsync(IDbConnection con, IDbTransaction tx, CancellationToken ct)
    {
        // retorna qual coluna existe (prioridade)
        var cols = new[]
        {
            "is_active",
            "active",
            "reg_status",
            "deleted_at_utc",
            "deleted_at"
        };

        foreach (var c in cols)
        {
            var n = await con.ExecuteScalarAsync<int>(
                new CommandDefinition(@"
SELECT COUNT(1)
FROM information_schema.columns
WHERE table_schema='ged'
  AND table_name='folder'
  AND column_name=@c;", new { c }, tx, cancellationToken: ct));

            if (n > 0) return c;
        }
        return null;
    }

    private async Task<string> DetectFolderStatusColumnAsync(
        IDbConnection con,
        IDbTransaction tx,
        CancellationToken ct)
    {
        // Possíveis padrões reais encontrados em bancos legados
        var candidates = new[]
        {
            "ativo",
            "active",
            "status",
            "reg_status",
            "situacao",
            "fl_ativo"
        };

        foreach (var col in candidates)
        {
            var exists = await con.ExecuteScalarAsync<int>(
                new CommandDefinition(@"
SELECT COUNT(1)
FROM information_schema.columns
WHERE table_schema = 'ged'
  AND table_name = 'folder'
  AND column_name = @col;
", new { col }, tx, cancellationToken: ct));

            if (exists > 0)
            {
                _logger.LogInformation("Coluna de status da pasta detectada: {Col}", col);
                return col;
            }
        }

        throw new InvalidOperationException(
            "Não foi encontrada nenhuma coluna de status em ged.folder. " +
            "Esperado algo como: ativo, active, reg_status, status, situacao, fl_ativo.");
    }

    private static string Q(string ident)
    {
        if (!SafeIdent.IsMatch(ident)) throw new InvalidOperationException($"Ident inválido: {ident}");
        return $@"""{ident}""";
    }

    private async Task<(string Col, string DataType)> DetectSoftDeleteColumnAsync(
        IDbConnection con, IDbTransaction tx, string tableName, CancellationToken ct)
    {
        // ordem de preferência
        var candidates = new[]
        {
            "deleted_at_utc", "deleted_at",      // timestamp
            "is_active", "active", "ativo",      // bool
            "reg_status", "status", "situacao"   // char/varchar
        };

        foreach (var col in candidates)
        {
            var row = await con.QueryFirstOrDefaultAsync<(string ColumnName, string DataType)>(
                new CommandDefinition(@"
SELECT column_name as ColumnName, data_type as DataType
FROM information_schema.columns
WHERE table_schema='ged'
  AND table_name=@t
  AND column_name=@c
LIMIT 1;", new { t = tableName, c = col }, tx, cancellationToken: ct));

            if (!string.IsNullOrWhiteSpace(row.ColumnName))
                return (row.ColumnName, row.DataType);
        }

        throw new InvalidOperationException($"Não encontrei coluna de soft delete/status em ged.{tableName}.");
    }

    private static string BuildSoftDeleteSet(string col, string dataType)
    {
        // dataType vem do information_schema: boolean, character varying, text, character, timestamp without time zone, etc.
        dataType = dataType.ToLowerInvariant();

        if (dataType.Contains("timestamp") || dataType.Contains("date"))
            return $"{Q(col)} = NOW()";

        if (dataType.Contains("bool"))
            return $"{Q(col)} = FALSE";

        // char/varchar/text/status: marcamos como D
        return $"{Q(col)} = 'D'";
    }
}
