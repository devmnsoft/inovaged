using Dapper;
using Xunit.Abstractions;
using InovaGed.Application.Audit;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Security;
using InovaGed.Domain.Primitives;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Ged.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Microsoft.Extensions.Caching.Memory;
using InovaGed.Infrastructure.Security;

namespace InovaGed.Application.Tests.Ged;

public sealed class DocumentMovePostgresTests
{
    private static readonly Guid RootId = Guid.Parse("f0000000-0000-0000-0000-000000000001");
    private readonly ITestOutputHelper _output;

    public DocumentMovePostgresTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Interface_atualiza_listagem_sem_timeout_fixo()
    {
        var root = FindRoot();
        var js = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "ged-document-move.js"));
        var html = File.ReadAllText(Path.Combine(root, "InovaGed.Web", "Views", "Ged", "Index.cshtml"));
        Assert.DoesNotContain(", 500)", js);
        Assert.Contains("refreshCurrentFolder", js);
        Assert.Contains("/Ged/Documents/UndoMove", js);
        Assert.Contains("WorkspaceUndo", js);
        Assert.Contains("moveAllOrNothing", html);
        Assert.Contains("moveResultDetails", html);
        var service = File.ReadAllText(Path.Combine(root, "InovaGed.Infrastructure", "Ged", "Documents", "DocumentMoveService.cs"));
        Assert.Contains("is not distinct from", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HISTORY_NOT_FOUND", service);
        Assert.Contains("A reversão não foi aplicada", service);
    }

    [LocalPgFact]
    public async Task Conferencia_antiga_e_historico_obrigatorio_falham_na_raiz_e_a_correcao_persiste()
    {
        await using var database = await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(newFolderNullable: false));
        var tenant = database.TenantId;
        var user = Guid.NewGuid();
        var origin = await InsertFolderAsync(database.Connection, tenant, "Origem");
        var documentId = await InsertDocumentAsync(database.Connection, tenant, origin, "Contrato");
        await InsertVersionAsync(database.Connection, tenant, documentId);

        string? oldSqlState = null;
        try
        {
            await database.Connection.ExecuteScalarAsync<int>("""
select count(1) from ged.document
where tenant_id = @tenantId and id = @documentId
  and ((@destinationFolderId is null and folder_id is null) or folder_id = @destinationFolderId)
""", new { tenantId = tenant, documentId, destinationFolderId = (Guid?)null });
        }
        catch (PostgresException ex)
        {
            oldSqlState = ex.SqlState;
        }

        _output.WriteLine("OLD_CONFIRMATION_SQLSTATE=" + (oldSqlState ?? "none"));
        Assert.True(oldSqlState is null || oldSqlState == "42P08", $"SQLSTATE inesperado na conferência antiga: {oldSqlState}");
        var confirmed = await database.Connection.ExecuteScalarAsync<int>("""
select count(1) from ged.document
where tenant_id = @tenantId and id = @documentId
  and folder_id is not distinct from cast(@destinationFolderId as uuid)
""", new { tenantId = tenant, documentId, destinationFolderId = origin });
        Assert.Equal(1, confirmed);

        var service = CreateService(database.Factory, new GrantPermissions());
        var blocked = await service.MoveAsync(tenant, user, "tester", documentId, RootId, "raiz", "SINGLE", true, "corr-root", "root-key", CancellationToken.None);
        Assert.False(blocked.Success);
        Assert.Equal("DATABASE_CONSTRAINT_ERROR", blocked.Error?.Code);
        Assert.Equal(origin, await FolderOfAsync(database.Connection, documentId));
        Assert.Equal(0, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history"));

        await database.ExecuteFileAsync("database/migrations/2026_11_08_document_move_root_history.sql");
        var moved = await service.MoveAsync(tenant, user, "tester", documentId, RootId, "raiz", "SINGLE", true, "corr-root", "root-key", CancellationToken.None);
        Assert.True(moved.Success);
        Assert.Null(await FolderOfAsync(database.Connection, documentId));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document"));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_version"));
        Assert.Equal("CLASS-1", await database.Connection.ExecuteScalarAsync<string>("select classification_code from ged.document where id=@documentId", new { documentId }));
        Assert.Equal("ocr-text", await database.Connection.ExecuteScalarAsync<string>("select ocr_text from ged.document_version where document_id=@documentId", new { documentId }));
        Assert.Equal("preview/path", await database.Connection.ExecuteScalarAsync<string>("select preview_path from ged.document_version where document_id=@documentId", new { documentId }));
        Assert.Equal("storage/path", await database.Connection.ExecuteScalarAsync<string>("select storage_path from ged.document_version where document_id=@documentId", new { documentId }));

        var history = await service.GetMoveHistoryAsync(tenant, documentId, CancellationToken.None);
        Assert.Single(history);
        Assert.Null(history[0].NewFolderId);
        Assert.Equal("Raiz", history[0].NewFolderName);
        Assert.Equal(origin, history[0].OldFolderId);
        Assert.Equal("corr-root", history[0].CorrelationId);
        Assert.Equal("raiz", history[0].Reason);
        Assert.Equal("SINGLE", history[0].Source);
        Assert.Equal(user, history[0].MovedBy);
    }

    [LocalPgFact]
    public async Task Move_individual_subpasta_mesmo_destino_e_preservacao()
    {
        await using var database = await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(newFolderNullable: true));
        var tenant = database.TenantId;
        var user = Guid.NewGuid();
        var parent = await InsertFolderAsync(database.Connection, tenant, "Pai");
        var child = await InsertFolderAsync(database.Connection, tenant, "Filha", parent);
        var documentId = await InsertDocumentAsync(database.Connection, tenant, parent, "Oficio");
        await InsertVersionAsync(database.Connection, tenant, documentId);
        await database.Connection.ExecuteAsync("insert into ged.roles values(@role,@tenant,'ADMIN'); insert into ged.user_roles values(@tenant,@user,@role)",new{role=Guid.NewGuid(),tenant,user});
        var service = CreateService(database.Factory, new GrantPermissions());

        var moved = await service.MoveAsync(tenant, user, "tester", documentId, child, "organizar", "SINGLE", true, "corr-sub", null, CancellationToken.None);
        Assert.True(moved.Success);
        Assert.Equal(child, await FolderOfAsync(database.Connection, documentId));
        Assert.Equal(parent, moved.Value!.OldFolderId);

        var again = await service.MoveAsync(tenant, user, "tester", documentId, child, "organizar", "SINGLE", true, "corr-sub-2", null, CancellationToken.None);
        Assert.True(again.Success);
        Assert.Equal("ALREADY_IN_DESTINATION", again.Value!.ErrorCode);
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history"));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document"));
        Assert.Equal("{\"k\":\"v\"}", await database.Connection.ExecuteScalarAsync<string>("select metadata_json from ged.document where id=@documentId", new { documentId }));
        Assert.Equal("PERM", await database.Connection.ExecuteScalarAsync<string>("select retention_code from ged.document where id=@documentId", new { documentId }));

        var folders = await service.SearchFoldersAsync(tenant, user, null, CancellationToken.None);
        Assert.Equal(2, folders.Count);
        Assert.All(folders, folder => Assert.True(folder.CanWrite));
    }

    [LocalPgFact]
    public async Task Move_lote_parcial_atomico_idempotente_e_volume()
    {
        await using var database = await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(newFolderNullable: true));
        var tenant = database.TenantId;
        var user = Guid.NewGuid();
        var origin = await InsertFolderAsync(database.Connection, tenant, "Origem");
        var destination = await InsertFolderAsync(database.Connection, tenant, "Destino");
        var service = CreateService(database.Factory, new GrantPermissions());

        var five = await InsertManyAsync(database.Connection, tenant, origin, 5);
        var fiveResult = await service.MoveBulkAsync(tenant, user, "tester", five, destination, "lote", "BULK", true, false, "five", "corr-five", CancellationToken.None);
        Assert.True(fiveResult.Success);
        Assert.Equal(5, fiveResult.Value!.SuccessCount);
        Assert.Equal(0, fiveResult.Value.FailCount);
        Assert.False(fiveResult.Value.RolledBack);
        Assert.Equal(5, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document where folder_id=@destination", new { destination }));

        var replay = await service.MoveBulkAsync(tenant, user, "tester", five, destination, "lote", "BULK", true, false, "five", "corr-five", CancellationToken.None);
        Assert.True(replay.Success);
        Assert.True(replay.Value!.IdempotentReplay);
        Assert.Equal(5, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history"));

        var conflict = await service.MoveBulkAsync(tenant, user, "tester", five, origin, "lote", "BULK", true, false, "five", "corr-five-b", CancellationToken.None);
        Assert.False(conflict.Success);
        Assert.Equal("MOVE_FAILED", conflict.Error?.Code);
        Assert.Equal(5, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document where folder_id=@destination", new { destination }));

        var partialIds = await InsertManyAsync(database.Connection, tenant, origin, 2);
        var partial = await service.MoveBulkAsync(tenant, user, "tester", [partialIds[0], Guid.NewGuid(), partialIds[1]], destination, "parcial", "BULK", true, false, "partial", "corr-partial", CancellationToken.None);
        Assert.True(partial.Success);
        Assert.Equal(2, partial.Value!.SuccessCount);
        Assert.Equal(1, partial.Value.FailCount);
        Assert.False(partial.Value.RolledBack);
        Assert.Contains(partial.Value.Items, item => item.ErrorCode == "DOCUMENT_NOT_FOUND");

        var atomicIds = await InsertManyAsync(database.Connection, tenant, origin, 2);
        var beforeHistory = await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history");
        var atomic = await service.MoveBulkAsync(tenant, user, "tester", [atomicIds[0], Guid.NewGuid(), atomicIds[1]], destination, "atomico", "BULK", true, true, "atomic", "corr-atomic", CancellationToken.None);
        Assert.True(atomic.Success);
        Assert.True(atomic.Value!.RolledBack);
        Assert.Equal(0, atomic.Value.SuccessCount);
        Assert.Equal(beforeHistory, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history"));
        Assert.Equal(origin, await FolderOfAsync(database.Connection, atomicIds[0]));
        Assert.Equal(origin, await FolderOfAsync(database.Connection, atomicIds[1]));

        var volumeOriginCount = await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document");
        var volume = await InsertManyAsync(database.Connection, tenant, origin, 200);
        var volumeResult = await service.MoveBulkAsync(tenant, user, "tester", volume, destination, "volume", "BULK", true, true, "volume-200", "corr-200", CancellationToken.None);
        Assert.True(volumeResult.Success);
        Assert.Equal(200, volumeResult.Value!.SuccessCount);
        Assert.False(volumeResult.Value.RolledBack);
        Assert.Equal(volumeOriginCount + 200, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document"));
        Assert.Equal(200, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_version where document_id = any(@volume)", new { volume }));
        Assert.Equal(200, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document where id = any(@volume) and folder_id=@destination and classification_code='CLASS-1'", new { volume, destination }));
    }

    [LocalPgFact]
    public async Task Move_recusa_permissao_sigilo_outro_tenant_pasta_ausente_e_inativa()
    {
        await using var database = await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(newFolderNullable: true));
        var tenant = database.TenantId;
        var otherTenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var origin = await InsertFolderAsync(database.Connection, tenant, "Origem");
        var destination = await InsertFolderAsync(database.Connection, tenant, "Destino");
        var inactive = await InsertFolderAsync(database.Connection, tenant, "Inativa", active: false);
        var normal = await InsertDocumentAsync(database.Connection, tenant, origin, "Normal");
        var secret = await InsertDocumentAsync(database.Connection, tenant, origin, "Sigiloso", "RESTRICTED");
        var foreign = await InsertDocumentAsync(database.Connection, otherTenant, null, "Outro");
        var denied = CreateService(database.Factory, new GrantPermissions());
        var allowedMove = CreateService(database.Factory, new GrantPermissions { Grants = { "GED_DOCUMENT_MOVE" } });

        var noPermission = await denied.MoveAsync(tenant, user, "tester", normal, destination, null, "SINGLE", false, "corr-deny", null, CancellationToken.None);
        Assert.False(noPermission.Success);
        Assert.Equal("ACCESS_DENIED", noPermission.Error?.Code);
        Assert.Equal(origin, await FolderOfAsync(database.Connection, normal));
        Assert.Equal(0, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history"));

        var confidential = await allowedMove.MoveAsync(tenant, user, "tester", secret, destination, null, "SINGLE", false, "corr-secret", null, CancellationToken.None);
        Assert.False(confidential.Success);
        Assert.Equal("ACCESS_DENIED", confidential.Error?.Code);
        Assert.Equal(origin, await FolderOfAsync(database.Connection, secret));

        var foreignResult = await denied.MoveAsync(tenant, user, "tester", foreign, destination, null, "SINGLE", true, "corr-foreign", null, CancellationToken.None);
        Assert.False(foreignResult.Success);
        Assert.Equal("DOCUMENT_NOT_FOUND", foreignResult.Error?.Code);
        Assert.Null(await FolderOfAsync(database.Connection, foreign));

        var missing = await denied.MoveAsync(tenant, user, "tester", normal, Guid.NewGuid(), null, "SINGLE", true, "corr-missing", null, CancellationToken.None);
        Assert.Equal("FOLDER_NOT_FOUND", missing.Error?.Code);
        var inactiveResult = await denied.MoveAsync(tenant, user, "tester", normal, inactive, null, "SINGLE", true, "corr-inactive", null, CancellationToken.None);
        Assert.Equal("FOLDER_INACTIVE", inactiveResult.Error?.Code);
        Assert.Equal(origin, await FolderOfAsync(database.Connection, normal));
    }

    [LocalPgFact]
    public async Task Move_reverte_constraint_concorrencia_e_mantem_sucesso_se_auditoria_falhar()
    {
        await using var database = await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(newFolderNullable: true));
        var tenant = database.TenantId;
        var user = Guid.NewGuid();
        var origin = await InsertFolderAsync(database.Connection, tenant, "Origem");
        var destination = await InsertFolderAsync(database.Connection, tenant, "Destino");
        var other = await InsertFolderAsync(database.Connection, tenant, "Outra");
        var documentId = await InsertDocumentAsync(database.Connection, tenant, origin, "Concorrente");
        await InsertVersionAsync(database.Connection, tenant, documentId);
        var service = CreateService(database.Factory, new GrantPermissions());

        await database.ExecuteAsync("""
create or replace function ged.reject_document_folder_change() returns trigger language plpgsql as $$
begin
  if new.folder_id is distinct from old.folder_id then
    raise exception using errcode = '23503', message = 'simulated folder fk';
  end if;
  return new;
end $$;
create trigger trg_reject_document_folder_change
before update on ged.document
for each row execute function ged.reject_document_folder_change();
""");
        var constraint = await service.MoveAsync(tenant, user, "tester", documentId, destination, null, "SINGLE", true, "corr-fk", null, CancellationToken.None);
        Assert.False(constraint.Success);
        Assert.Equal("DATABASE_CONSTRAINT_ERROR", constraint.Error?.Code);
        Assert.Equal(origin, await FolderOfAsync(database.Connection, documentId));
        await database.ExecuteAsync("drop trigger trg_reject_document_folder_change on ged.document");

        await database.ExecuteAsync("""
create or replace function ged.reject_document_folder_change() returns trigger language plpgsql as $$
begin
  if new.folder_id is distinct from old.folder_id then
    raise exception using errcode = '40001', message = 'concurrent move';
  end if;
  return new;
end $$;
create trigger trg_reject_document_folder_change
before update on ged.document
for each row execute function ged.reject_document_folder_change();
""");
        var concurrentCode = await service.MoveAsync(tenant, user, "tester", documentId, destination, null, "SINGLE", true, "corr-40001", null, CancellationToken.None);
        Assert.Equal("CONCURRENT_MODIFICATION", concurrentCode.Error?.Code);
        Assert.Equal(origin, await FolderOfAsync(database.Connection, documentId));
        await database.ExecuteAsync("drop trigger trg_reject_document_folder_change on ged.document");

        var auditService = CreateService(database.Factory, new GrantPermissions(), new ThrowingAudit());
        var audited = await auditService.MoveAsync(tenant, user, "tester", documentId, destination, "auditoria", "SINGLE", true, "corr-audit", "audit-key", CancellationToken.None);
        Assert.True(audited.Success);
        Assert.Equal(destination, await FolderOfAsync(database.Connection, documentId));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history"));

        var left = await InsertDocumentAsync(database.Connection, tenant, origin, "Esquerda");
        var right = await InsertDocumentAsync(database.Connection, tenant, origin, "Direita");
        var parallel = await Task.WhenAll(
            service.MoveAsync(tenant, user, "tester", left, destination, null, "SINGLE", true, "corr-a", null, CancellationToken.None),
            service.MoveAsync(tenant, user, "tester", left, other, null, "SINGLE", true, "corr-b", null, CancellationToken.None));
        Assert.Equal(2, parallel.Count(result => result.Success));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document where id=@left", new { left }));
        var leftFolder = await FolderOfAsync(database.Connection, left);
        Assert.True(leftFolder == destination || leftFolder == other);

        var duplicate = await InsertDocumentAsync(database.Connection, tenant, origin, "Duplicado");
        var duplicated = await Task.WhenAll(
            service.MoveAsync(tenant, user, "tester", duplicate, destination, null, "SINGLE", true, "corr-dup-a", "same-key", CancellationToken.None),
            service.MoveAsync(tenant, user, "tester", duplicate, destination, null, "SINGLE", true, "corr-dup-b", "same-key", CancellationToken.None));
        Assert.Contains(duplicated, result => result.Success);
        Assert.Equal(destination, await FolderOfAsync(database.Connection, duplicate));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history where document_id=@duplicate", new { duplicate }));
        Assert.Equal(origin, await FolderOfAsync(database.Connection, right));
    }

    [LocalPgFact]
    public async Task Desfazer_grava_novo_historico_e_recusa_concorrencia_bloqueio_e_pasta_indisponivel()
    {
        await using var database = await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(newFolderNullable: true));
        var tenant = database.TenantId;
        var user = Guid.NewGuid();
        var origin = await InsertFolderAsync(database.Connection, tenant, "Origem");
        var destination = await InsertFolderAsync(database.Connection, tenant, "Destino");
        var other = await InsertFolderAsync(database.Connection, tenant, "Outra");
        var documentId = await InsertDocumentAsync(database.Connection, tenant, origin, "Reversivel");
        await InsertVersionAsync(database.Connection, tenant, documentId);
        var service = CreateService(database.Factory, new GrantPermissions());

        var moved = await service.MoveAsync(tenant, user, "tester", documentId, destination, "organizar", "SINGLE", true, "corr-undo", "move-undo", CancellationToken.None);
        Assert.True(moved.Success);
        Assert.NotNull(moved.Value!.HistoryId);

        var undone = await service.UndoAsync(tenant, user, "tester", documentId, moved.Value.HistoryId, null, true, "corr-undo-back", null, CancellationToken.None);
        Assert.True(undone.Success);
        Assert.Equal(origin, await FolderOfAsync(database.Connection, documentId));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document"));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_version"));
        Assert.Equal("CLASS-1", await database.Connection.ExecuteScalarAsync<string>("select classification_code from ged.document where id=@documentId", new { documentId }));
        Assert.Equal("ocr-text", await database.Connection.ExecuteScalarAsync<string>("select ocr_text from ged.document_version where document_id=@documentId", new { documentId }));
        Assert.Equal("preview/path", await database.Connection.ExecuteScalarAsync<string>("select preview_path from ged.document_version where document_id=@documentId", new { documentId }));
        var history = await service.GetMoveHistoryAsync(tenant, documentId, CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, row => row.Id == moved.Value.HistoryId && row.Source == "SINGLE");
        Assert.Contains(history, row => row.Source == "UNDO" && row.NewFolderId == origin && row.OldFolderId == destination);

        var repeated = await service.UndoAsync(tenant, user, "tester", documentId, moved.Value.HistoryId, null, true, "corr-undo-repeat", null, CancellationToken.None);
        Assert.True(repeated.Success);
        Assert.Equal("ALREADY_IN_DESTINATION", repeated.Value!.ErrorCode);
        Assert.Equal(2, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history where document_id=@documentId", new { documentId }));

        var movedAgain = await service.MoveAsync(tenant, user, "tester", documentId, destination, "organizar", "SINGLE", true, "corr-undo-2", null, CancellationToken.None);
        var away = await service.MoveAsync(tenant, user, "tester", documentId, other, "seguir", "SINGLE", true, "corr-away", null, CancellationToken.None);
        Assert.True(away.Success);
        var beforeConcurrent = await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history where document_id=@documentId", new { documentId });
        var concurrent = await service.UndoAsync(tenant, user, "tester", documentId, movedAgain.Value!.HistoryId, null, true, "corr-concurrent", null, CancellationToken.None);
        Assert.False(concurrent.Success);
        Assert.Equal("CONCURRENT_MODIFICATION", concurrent.Error?.Code);
        Assert.Equal(other, await FolderOfAsync(database.Connection, documentId));
        Assert.Equal(beforeConcurrent, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document_folder_move_history where document_id=@documentId", new { documentId }));

        var held = await InsertDocumentAsync(database.Connection, tenant, origin, "Bloqueado");
        var heldMove = await service.MoveAsync(tenant, user, "tester", held, destination, null, "SINGLE", true, "corr-hold", null, CancellationToken.None);
        await database.Connection.ExecuteAsync("alter table ged.document add column retention_hold boolean not null default false");
        await database.Connection.ExecuteAsync("update ged.document set retention_hold = true where id = @held", new { held });
        var blocked = await service.UndoAsync(tenant, user, "tester", held, heldMove.Value!.HistoryId, null, true, "corr-hold-undo", null, CancellationToken.None);
        Assert.False(blocked.Success);
        Assert.Equal("DOCUMENT_LOCKED", blocked.Error?.Code);
        Assert.Equal(destination, await FolderOfAsync(database.Connection, held));

        var missingFolder = await InsertDocumentAsync(database.Connection, tenant, origin, "Sem pasta");
        var missingMove = await service.MoveAsync(tenant, user, "tester", missingFolder, destination, null, "SINGLE", true, "corr-missing-folder", null, CancellationToken.None);
        await database.Connection.ExecuteAsync("delete from ged.folder where id = @origin", new { origin });
        var missing = await service.UndoAsync(tenant, user, "tester", missingFolder, missingMove.Value!.HistoryId, null, true, "corr-missing-undo", null, CancellationToken.None);
        Assert.Equal("FOLDER_NOT_FOUND", missing.Error?.Code);
        Assert.Equal(destination, await FolderOfAsync(database.Connection, missingFolder));

        var inactiveDoc = await InsertDocumentAsync(database.Connection, tenant, destination, "Inativo");
        var inactiveMove = await service.MoveAsync(tenant, user, "tester", inactiveDoc, other, null, "SINGLE", true, "corr-inactive-move", null, CancellationToken.None);
        await database.Connection.ExecuteAsync("update ged.folder set is_active = false where id = @destination", new { destination });
        var inactive = await service.UndoAsync(tenant, user, "tester", inactiveDoc, inactiveMove.Value!.HistoryId, null, true, "corr-inactive-undo", null, CancellationToken.None);
        Assert.Equal("FOLDER_INACTIVE", inactive.Error?.Code);
        Assert.Equal(other, await FolderOfAsync(database.Connection, inactiveDoc));

        await database.Connection.ExecuteAsync("update ged.folder set is_active = true where id = @destination", new { destination });
        var deniedDoc = await InsertDocumentAsync(database.Connection, tenant, other, "Negado");
        var deniedMove = await service.MoveAsync(tenant, user, "tester", deniedDoc, destination, null, "SINGLE", true, "corr-deny-move", null, CancellationToken.None);
        var denied = CreateService(database.Factory, new GrantPermissions());
        var deniedUndo = await denied.UndoAsync(tenant, user, "tester", deniedDoc, deniedMove.Value!.HistoryId, null, false, "corr-deny-undo", null, CancellationToken.None);
        Assert.Equal("ACCESS_DENIED", deniedUndo.Error?.Code);
        Assert.Equal(destination, await FolderOfAsync(database.Connection, deniedDoc));

        var rooted = await service.MoveAsync(tenant, user, "tester", deniedDoc, RootId, "raiz", "SINGLE", true, "corr-root-undo", null, CancellationToken.None);
        Assert.True(rooted.Success);
        Assert.Null(await FolderOfAsync(database.Connection, deniedDoc));
        var back = await service.UndoAsync(tenant, user, "tester", deniedDoc, rooted.Value!.HistoryId, null, true, "corr-root-back", null, CancellationToken.None);
        Assert.True(back.Success);
        Assert.Equal(destination, await FolderOfAsync(database.Connection, deniedDoc));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.document where id=@deniedDoc", new { deniedDoc }));
    }

    [LocalPgFact]
    public async Task Virtual_destinations_resolve_on_server_and_legacy_roles_are_recognized()
    {
        await using var database = await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(true));
        var origin = await InsertFolderAsync(database.Connection, database.TenantId, "Origem");
        var destination = await InsertFolderAsync(database.Connection, database.TenantId, "Destino");
        var document = await InsertDocumentAsync(database.Connection, database.TenantId, origin, "Documento");
        var user = Guid.NewGuid(); var role = Guid.NewGuid();
        var virtualId = Guid.Parse("f0000000-0000-0000-0000-000000000009");
        await database.Connection.ExecuteAsync("""
create table ged.folder_virtual_map(tenant_id uuid,virtual_folder_id uuid,real_folder_id uuid,reg_status text);
insert into ged.folder_virtual_map values(@tenant,@virtualId,@destination,'A');
insert into ged.roles values(@role,@tenant,'ARQUIVISTA_OPHIR');
insert into ged.user_roles values(@tenant,@user,@role);
""",new {tenant=database.TenantId,virtualId,destination,user,role});
        var service=CreateService(database.Factory,new GrantPermissions());
        var moved=await service.MoveAsync(database.TenantId,user,"tester",document,virtualId,null,"SINGLE",false,default);
        Assert.True(moved.Success);
        Assert.Equal(destination,await FolderOfAsync(database.Connection,document));
        var available=await service.SearchFoldersAsync(database.TenantId,user,null,default);
        Assert.All(available,x=>Assert.True(x.CanWrite));
        var denied=await service.SearchFoldersAsync(database.TenantId,Guid.NewGuid(),null,default);
        Assert.All(denied,x=>Assert.False(x.CanWrite));
        var missing=await service.MoveAsync(database.TenantId,user,"tester",document,Guid.Parse("f0000000-0000-0000-0000-000000000099"),null,"SINGLE",false,default);
        Assert.Equal("FOLDER_NOT_FOUND",missing.Error?.Code);
        Assert.Equal(destination,await FolderOfAsync(database.Connection,document));
    }

    [LocalPgFact]
    public async Task Mixed_document_access_moves_authorized_items_only_and_atomic_batch_rolls_back()
    {
        await using var database = await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(true));
        var origin=await InsertFolderAsync(database.Connection,database.TenantId,"Origem");
        var destination=await InsertFolderAsync(database.Connection,database.TenantId,"Destino");
        var first=await InsertDocumentAsync(database.Connection,database.TenantId,origin,"Permitido");
        var second=await InsertDocumentAsync(database.Connection,database.TenantId,origin,"Negado");
        var access=new DocumentAccess();access.Denied.Add(second);
        var service=new DocumentMoveService(database.Factory,new NoopAudit(),NullLogger<DocumentMoveService>.Instance,new GrantPermissions(),access);
        var atomic=await service.MoveBulkAsync(database.TenantId,Guid.NewGuid(),"tester",new[]{first,second},destination,null,"BULK",true,true,"atomic-acl","corr",default);
        Assert.True(atomic.Value!.RolledBack);
        Assert.Equal(origin,await FolderOfAsync(database.Connection,first));
        Assert.Equal(origin,await FolderOfAsync(database.Connection,second));
        var partial=await service.MoveBulkAsync(database.TenantId,Guid.NewGuid(),"tester",new[]{first,second},destination,null,"BULK",true,false,"partial-acl","corr",default);
        Assert.Equal(1,partial.Value!.SuccessCount);
        Assert.Equal(1,partial.Value.FailCount);
        Assert.Equal("ACCESS_DENIED",partial.Value.Items.Single(x=>!x.Success).ErrorCode);
        Assert.Equal(destination,await FolderOfAsync(database.Connection,first));
        Assert.Equal(origin,await FolderOfAsync(database.Connection,second));
        access.Denied.Add(first);
        var repeat=await service.MoveAsync(database.TenantId,Guid.NewGuid(),"tester",first,destination,null,"SINGLE",true,"corr","partial-acl",default);
        Assert.Equal("ACCESS_DENIED",repeat.Error?.Code);
    }

    [LocalPgFact]
    public async Task Canonical_identity_permissions_authorize_move_without_legacy_user_role_copy()
    {
        await using var database=await MoveDatabase.CreateAsync();
        await database.ExecuteAsync(SchemaSql(true));
        var user=Guid.NewGuid();var role=Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
create table ged.role_permission(tenant_id uuid,role_id uuid,permission_code text,reg_status text);
insert into ged.app_role values(@role,@tenant,'OPERADOR');
insert into ged.app_user_role values(@user,@role);
insert into ged.role_permission values(@tenant,@role,'GED_DOCUMENT_MOVE','A');
""",new {tenant=database.TenantId,user,role});
        using var cache=new MemoryCache(new MemoryCacheOptions());
        var permissions=new PermissionService(database.Factory,cache,NullLogger<PermissionService>.Instance);
        Assert.True(await permissions.HasAsync(database.TenantId,user,"GED_DOCUMENT_MOVE",default));
        Assert.False(await permissions.HasAsync(Guid.NewGuid(),user,"GED_DOCUMENT_MOVE",default));
        var origin=await InsertFolderAsync(database.Connection,database.TenantId,"Origem");
        var destination=await InsertFolderAsync(database.Connection,database.TenantId,"Destino");
        var document=await InsertDocumentAsync(database.Connection,database.TenantId,origin,"Documento");
        var service=CreateService(database.Factory,permissions);
        Assert.True((await service.MoveAsync(database.TenantId,user,"tester",document,destination,null,"SINGLE",false,default)).Success);
        Assert.Equal(destination,await FolderOfAsync(database.Connection,document));
    }

    private static DocumentMoveService CreateService(NpgsqlConnectionFactory factory, IPermissionService permissions, IAuditWriter? audit = null)
        => new(factory, audit ?? new NoopAudit(), NullLogger<DocumentMoveService>.Instance, permissions, new DocumentAccess());

    private static async Task<Guid> InsertFolderAsync(NpgsqlConnection connection, Guid tenantId, string name, Guid? parentId = null, bool active = true)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync("insert into ged.folder (id, tenant_id, parent_id, name, is_active, reg_status) values (@id, @tenantId, @parentId, @name, @active, 'A')", new { id, tenantId, parentId, name, active });
        return id;
    }

    private static async Task<Guid> InsertDocumentAsync(NpgsqlConnection connection, Guid tenantId, Guid? folderId, string title, string visibility = "PUBLIC")
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync("""
insert into ged.document (id, tenant_id, folder_id, title, status, visibility, security_level, classification_code, metadata_json, retention_code)
values (@id, @tenantId, @folderId, @title, 'ACTIVE', @visibility, null, 'CLASS-1', '{"k":"v"}', 'PERM')
""", new { id, tenantId, folderId, title, visibility });
        return id;
    }

    private static async Task InsertVersionAsync(NpgsqlConnection connection, Guid tenantId, Guid documentId)
        => await connection.ExecuteAsync("""
insert into ged.document_version (id, tenant_id, document_id, version_no, storage_path, ocr_text, preview_path, classification_version_id)
values (@id, @tenantId, @documentId, 1, 'storage/path', 'ocr-text', 'preview/path', @classification)
""", new { id = Guid.NewGuid(), tenantId, documentId, classification = Guid.Parse("00000000-0000-0000-0000-000000000099") });

    private static async Task<Guid[]> InsertManyAsync(NpgsqlConnection connection, Guid tenantId, Guid folderId, int count)
    {
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = await InsertDocumentAsync(connection, tenantId, folderId, "Lote " + i);
            await InsertVersionAsync(connection, tenantId, ids[i]);
        }
        return ids;
    }

    private static Task<Guid?> FolderOfAsync(NpgsqlConnection connection, Guid documentId)
        => connection.ExecuteScalarAsync<Guid?>("select folder_id from ged.document where id=@documentId", new { documentId });

    private static string SchemaSql(bool newFolderNullable) => $"""
create schema if not exists ged;
create table ged.folder (
    id uuid primary key,
    tenant_id uuid not null,
    parent_id uuid null references ged.folder(id),
    name text not null,
    is_active boolean not null default true,
    reg_status char(1) not null default 'A'
);
create table ged.document (
    id uuid primary key,
    tenant_id uuid not null,
    folder_id uuid null references ged.folder(id),
    title text not null,
    status text not null,
    visibility text not null,
    security_level text null,
    classification_code text null,
    metadata_json text null,
    retention_code text null,
    updated_at timestamptz null,
    updated_by uuid null
);
create table ged.document_version (
    id uuid primary key,
    tenant_id uuid not null,
    document_id uuid not null references ged.document(id),
    version_no integer not null,
    storage_path text not null,
    ocr_text text null,
    preview_path text null,
    classification_version_id uuid null
);
create table ged.document_folder_move_history (
    id uuid primary key,
    tenant_id uuid not null,
    document_id uuid not null,
    document_version_id uuid null,
    old_folder_id uuid null,
    new_folder_id uuid {(newFolderNullable ? "null" : "not null")},
    moved_by uuid not null,
    moved_by_name text null,
    moved_at timestamptz not null default now(),
    reason text null,
    batch_id uuid null,
    source text null,
    correlation_id text null,
    reg_status char(1) not null default 'A'
);
create unique index ux_document_folder_move_history_tenant_batch_document
    on ged.document_folder_move_history (tenant_id, batch_id, document_id)
    where batch_id is not null and reg_status = 'A';
create table ged.app_role (id uuid primary key, tenant_id uuid not null, normalized_name text not null);
create table ged.roles (id uuid primary key, tenant_id uuid not null, name text not null);
create table ged.app_user_role (user_id uuid not null, role_id uuid not null);
create table ged.user_roles (tenant_id uuid not null, user_id uuid not null, role_id uuid not null references ged.roles(id));
""";

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class DocumentAccess : IAbacAuthorizationService
    {
        public HashSet<Guid> Denied { get; } = [];
        public Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string,string> attributes, CancellationToken ct) => Task.FromResult(!Denied.Contains(documentId));
    }

    private sealed class GrantPermissions : IPermissionService
    {
        public HashSet<string> Grants { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task<bool> HasAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct) => Task.FromResult(Grants.Contains(permissionCode));
    }

    private sealed class NoopAudit : IAuditWriter
    {
        public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data, CancellationToken ct) => Task.FromResult(Result.Ok());
    }

    private sealed class ThrowingAudit : IAuditWriter
    {
        public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct) => throw new InvalidOperationException("auditoria indisponível");
        public Task<Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data, CancellationToken ct) => throw new InvalidOperationException("auditoria indisponível");
    }

    private sealed class MoveDatabase : IAsyncDisposable
    {
        private readonly string _adminDsn;
        private readonly string _name;
        public NpgsqlConnection Connection { get; }
        public NpgsqlConnectionFactory Factory { get; }
        public Guid TenantId { get; } = Guid.NewGuid();

        private MoveDatabase(string adminDsn, string name, NpgsqlConnection connection, NpgsqlConnectionFactory factory)
        {
            _adminDsn = adminDsn;
            _name = name;
            Connection = connection;
            Factory = factory;
        }

        public static async Task<MoveDatabase> CreateAsync()
        {
            var name = "ig_move_" + Guid.NewGuid().ToString("N");
            var adminBuilder = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"create database {name}");
            var builder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString) { Database = name };
            var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return new MoveDatabase(adminBuilder.ConnectionString, name, connection, new NpgsqlConnectionFactory(builder.ConnectionString));
        }

        public async Task ExecuteFileAsync(string relativePath)
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(FindRoot(), relativePath));
            await Connection.ExecuteAsync(sql);
        }

        public Task ExecuteAsync(string sql) => Connection.ExecuteAsync(sql);

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminDsn);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"drop database if exists {_name} with (force)");
        }
    }
}
