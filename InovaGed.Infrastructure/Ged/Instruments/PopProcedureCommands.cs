using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Instruments;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

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

    public async Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, PopProcedureCreateVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (string.IsNullOrWhiteSpace(vm.Code)) return Result<Guid>.Fail("CODE", "Código é obrigatório.");
            if (string.IsNullOrWhiteSpace(vm.Title)) return Result<Guid>.Fail("TITLE", "Título é obrigatório.");
            if (string.IsNullOrWhiteSpace(vm.ContentMd)) return Result<Guid>.Fail("CONTENT", "Conteúdo é obrigatório.");

            await using var conn = await _db.OpenAsync(ct);

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
                code = vm.Code.Trim(),
                title = vm.Title.Trim(),
                content = vm.ContentMd,
                active = vm.IsActive,
                by = userId
            }, cancellationToken: ct));

            _ = await _audit.WriteAsync(tenantId, userId, "CREATE", "pop_procedure", id,
                "POP criado", null, null, new { vm.Code, vm.Title }, ct);

            return Result<Guid>.Ok(id);
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
            await using var conn = await _db.OpenAsync(ct);

            const string sql = @"
update ged.pop_procedure
set code=@code,
    title=@title,
    content_md=@content,
    is_active=@active,
    updated_at=now(),
    updated_by=@by
where tenant_id=@tenant_id and id=@id and reg_status='A';
";
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                id,
                code = vm.Code.Trim(),
                title = vm.Title.Trim(),
                content = vm.ContentMd,
                active = vm.IsActive,
                by = userId
            }, cancellationToken: ct));

            if (rows == 0) return Result.Fail("NOTFOUND", "POP não encontrado.");

            _ = await _audit.WriteAsync(tenantId, userId, "UPDATE", "pop_procedure", id,
                "POP atualizado", null, null, new { vm.Code, vm.Title }, ct);

            return Result.Ok();
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
            if (vm.ProcedureId == Guid.Empty) return Result<Guid>.Fail("ID", "ProcedureId inválido.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            const string nextNo = @"
select coalesce(max(version_no),0)+1
from ged.pop_procedure_version
where tenant_id=@tenant_id and procedure_id=@pid and reg_status='A';
";
            var no = await conn.ExecuteScalarAsync<int>(new CommandDefinition(nextNo, new { tenant_id = tenantId, pid = vm.ProcedureId }, transaction: tx, cancellationToken: ct));

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
                title = vm.Title.Trim(),
                content = vm.ContentMd,
                by = userId,
                notes = vm.Notes
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            _ = await _audit.WriteAsync(tenantId, userId, "VERSION_CREATE", "pop_procedure_version", verId,
                "Publicação de versão do POP", null, null, new { no, vm.Title, vm.ProcedureId }, ct);

            return Result<Guid>.Ok(verId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PopProcedureCommands.PublishVersionAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("POPVER", "Falha ao publicar versão do POP.");
        }
    }
}