using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Instruments;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Ged.Instruments;

public sealed class PopProcedureCommands : IPopProcedureCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<PopProcedureCommands> _logger;

    public PopProcedureCommands(IDbConnectionFactory db, IAuditWriter audit, ILogger<PopProcedureCommands> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    private static string NormCode(string? code)
        => (code ?? "").Trim(); // se você quiser: .ToUpperInvariant()

    private static string NormTitle(string? title)
        => (title ?? "").Trim();

    public async Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, PopProcedureCreateVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");

            var code = (vm.Code ?? "").Trim();
            var title = (vm.Title ?? "").Trim();
            var content = vm.ContentMd ?? "";

            if (string.IsNullOrWhiteSpace(code)) return Result<Guid>.Fail("CODE", "Código é obrigatório.");
            if (string.IsNullOrWhiteSpace(title)) return Result<Guid>.Fail("TITLE", "Título é obrigatório.");
            if (string.IsNullOrWhiteSpace(content)) return Result<Guid>.Fail("CONTENT", "Conteúdo é obrigatório.");

            await using var conn = await _db.OpenAsync(ct);

            // Pré-checagem de duplicidade (mensagem boa pro usuário)
            const string existsSql = @"
select 1
from ged.pop_procedure p
where p.tenant_id = @tenant_id
  and p.code = @code
  and p.reg_status = 'A'
limit 1;
";
            var exists = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                existsSql,
                new { tenant_id = tenantId, code },
                cancellationToken: ct));

            if (exists.HasValue)
                return Result<Guid>.Fail("DUPLICATE_CODE", $"Já existe um POP com o código '{code}'.");

            const string sql = @"
insert into ged.pop_procedure
(id, tenant_id, code, title, content_md, is_active, created_at, created_by, reg_date, reg_status)
values
(gen_random_uuid(), @tenant_id, @code, @title, @content, @active, now(), @by, now(), 'A')
returning id;
";
            var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                code,
                title,
                content,
                active = vm.IsActive,
                by = userId
            }, cancellationToken: ct));

            _ = await _audit.WriteAsync(
                tenantId,
                userId,
                "CREATE",
                "pop_procedure",
                id,
                "POP criado",
                null,
                null,
                new { Code = code, Title = title },
                ct);

            return Result<Guid>.Ok(id);
        }
        catch (PostgresException pg) when (pg.SqlState == "23505")
        {
            // duplicidade
            _logger.LogWarning(pg, "POP duplicate key. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("DUPLICATE_CODE", "Já existe um POP com este código para este tenant.");
        }
        catch (PostgresException pg) when (pg.SqlState == "23502")
        {
            // not-null violation
            _logger.LogError(pg, "POP not-null violation. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("DB_NOTNULL", "Campo obrigatório no banco não foi informado (NOT NULL).");
        }
        catch (PostgresException pg) when (pg.SqlState == "42703")
        {
            // coluna inexistente
            _logger.LogError(pg, "POP missing column. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("DB_COLUMN", "Estrutura do banco incompatível (coluna não existe). Rode as migrations/SQL do módulo POP.");
        }
        catch (PostgresException pg) when (pg.SqlState == "42883")
        {
            // função inexistente (ex.: gen_random_uuid)
            _logger.LogError(pg, "POP missing function. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("DB_FUNCTION", "Função do banco não encontrada (provável falta de extensão pgcrypto para gen_random_uuid).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PopProcedureCommands.CreateAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("POP", "Falha ao criar POP.");
        }
    }
    public async Task<Result> UpdateAsync(Guid tenantId, Guid id, Guid? userId, PopProcedureUpdateVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (id == Guid.Empty) return Result.Fail("ID", "Id inválido.");

            var code = NormCode(vm.Code);
            var title = NormTitle(vm.Title);

            if (string.IsNullOrWhiteSpace(code)) return Result.Fail("CODE", "Código é obrigatório.");
            if (string.IsNullOrWhiteSpace(title)) return Result.Fail("TITLE", "Título é obrigatório.");
            if (string.IsNullOrWhiteSpace(vm.ContentMd)) return Result.Fail("CONTENT", "Conteúdo é obrigatório.");

            await using var conn = await _db.OpenAsync(ct);

            // 1) Busca o registro atual (pra auditoria e pra saber se existe)
            const string getSql = @"
select id, code, title, content_md as ContentMd, is_active as IsActive
from ged.pop_procedure
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
            var old = await conn.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                getSql,
                new { tenant_id = tenantId, id },
                cancellationToken: ct));

            if (old is null)
                return Result.Fail("NOTFOUND", "POP não encontrado.");

            // 2) Checa duplicidade de code (exclui o próprio id)
            const string dupSql = @"
select 1
from ged.pop_procedure p
where p.tenant_id = @tenant_id
  and p.code = @code
  and p.id <> @id
  and p.reg_status = 'A'
limit 1;
";
            var dup = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                dupSql,
                new { tenant_id = tenantId, id, code },
                cancellationToken: ct));

            if (dup.HasValue)
                return Result.Fail("DUPLICATE_CODE", $"Já existe um POP com o código '{code}'.");

            // 3) Atualiza
            const string updSql = @"
update ged.pop_procedure
set code=@code,
    title=@title,
    content_md=@content,
    is_active=@active,
    updated_at=now(),
    updated_by=@by
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(updSql, new
            {
                tenant_id = tenantId,
                id,
                code,
                title,
                content = vm.ContentMd,
                active = vm.IsActive,
                by = userId
            }, cancellationToken: ct));

            if (rows == 0)
                return Result.Fail("NOTFOUND", "POP não encontrado.");

            // Auditoria com before/after (melhor que null)
            _ = await _audit.WriteAsync(
      tenantId,
      userId,
      "UPDATE",
      "pop_procedure",
      id,
      "POP atualizado",
      null,
      null,
      new
      {
          before = new
          {
              Code = (string)old.code,
              Title = (string)old.title,
              ContentMd = (string)old.contentmd,
              IsActive = (bool)old.isactive
          },
          after = new
          {
              Code = code,
              Title = title,
              ContentMd = vm.ContentMd,
              IsActive = vm.IsActive
          }
      },
      ct
  );

            return Result.Ok();
        }
        catch (PostgresException pg) when (pg.SqlState == "23505")
        {
            // Se ainda bater (race condition), devolve mensagem clara
            _logger.LogWarning(pg, "POP duplicate key on update. Tenant={Tenant} Id={Id}", tenantId, id);
            return Result.Fail("DUPLICATE_CODE", "Já existe um POP com este código para este tenant.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PopProcedureCommands.UpdateAsync failed. Tenant={Tenant}", tenantId);
            return Result.Fail("POP", "Falha ao atualizar POP.");
        }
    }

    public async Task<Result<Guid>> PublishVersionAsync(Guid tenantId, Guid? userId, PublishPopVersionVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm.ProcedureId == Guid.Empty) return Result<Guid>.Fail("ID", "ProcedureId inválido.");

            var title = NormTitle(vm.Title);
            if (string.IsNullOrWhiteSpace(title)) return Result<Guid>.Fail("TITLE", "Título é obrigatório.");
            if (string.IsNullOrWhiteSpace(vm.ContentMd)) return Result<Guid>.Fail("CONTENT", "Conteúdo é obrigatório.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string nextNo = @"
select coalesce(max(version_no),0)+1
from ged.pop_procedure_version
where tenant_id=@tenant_id and procedure_id=@pid and reg_status='A';
";
            var no = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                nextNo,
                new { tenant_id = tenantId, pid = vm.ProcedureId },
                transaction: tx,
                cancellationToken: ct));

            const string ins = @"
insert into ged.pop_procedure_version
(id, tenant_id, procedure_id, version_no, title, content_md, published_at, published_by, notes, reg_status)
values
(gen_random_uuid(), @tenant_id, @pid, @no, @title, @content, now(), @by, @notes, 'A')
returning id;
";
            var verId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(ins, new
            {
                tenant_id = tenantId,
                pid = vm.ProcedureId,
                no,
                title,
                content = vm.ContentMd,
                by = userId,
                notes = vm.Notes
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            _ = await _audit.WriteAsync(tenantId, userId, "VERSION_CREATE", "pop_procedure_version", verId,
                "Publicação de versão do POP", null, null, new { no, Title = title, vm.ProcedureId }, ct);

            return Result<Guid>.Ok(verId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PopProcedureCommands.PublishVersionAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("POPVER", "Falha ao publicar versão do POP.");
        }
    }
}