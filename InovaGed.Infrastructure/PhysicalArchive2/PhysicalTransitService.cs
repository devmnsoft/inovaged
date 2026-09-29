using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.PhysicalArchive2;
using Npgsql;

namespace InovaGed.Infrastructure.PhysicalArchive2;

/// <summary>
/// Bloco B: trânsito físico de prontuários (DAME/arquivo central ↔ setores hospitalares).
/// Um lote (ged.physical_loan_batch) agrupa N saídas de prontuário (uma linha ged.physical_loan por documento,
/// coluna document_id), com trilha de custódia em ged.physical_custody_event.
/// RN01: guarda-serviço (seleção de abertos) + índice único parcial ux_physical_loan_open_document (banco)
/// impedem um segundo empréstimo aberto para o mesmo prontuário; o lote é gravado em transação única.
/// </summary>
public sealed class PhysicalTransitService(IDbConnectionFactory factory) : IPhysicalTransitService
{
    private const int MaxItemsPerBatch = 100;

    public async Task<IReadOnlyList<TransitSectorOption>> SectorsAsync(Guid tenantId, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var rows = await db.QueryAsync<SectorRow>(new CommandDefinition(
            "select id,nome,sigla from ged.protocolo_setor where tenant_id=@t and reg_status='A' and ativo=true order by ordem,nome",
            new { t = tenantId }, cancellationToken: ct));
        return rows.Select(r => new TransitSectorOption(r.Id, r.Nome, r.Sigla)).ToList();
    }

    public async Task<IReadOnlyList<TransitCatalogItem>> CatalogAsync(Guid tenantId, string? q, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        const string sql = """
select d.id Id, coalesce(d.code,'SEM-CODIGO') Code, left(coalesce(d.title,'Sem título'),120) Title,
 exists(select 1 from ged.physical_loan l where l.tenant_id=d.tenant_id and l.document_id=d.id and l.reg_status='A' and l.status='OPEN') IsOut
from ged.document d
where d.tenant_id=@t and d.reg_status='A' and d.deleted_at is null
 and (@q is null or @q='' or d.code ilike '%'||@q||'%' or coalesce(d.title,'') ilike '%'||@q||'%')
order by d.code limit 200
""";
        var rows = await db.QueryAsync<CatalogRow>(new CommandDefinition(sql, new { t = tenantId, q = q?.Trim() }, cancellationToken: ct));
        return rows.Select(r => new TransitCatalogItem(r.Id, r.Code, r.Title, r.IsOut)).ToList();
    }

    public async Task<TransitCheckoutResult> CheckoutAsync(
        Guid tenantId, Guid? sectorId, string? sectorName, string? carrierName, string? carrierId, string? reason,
        IReadOnlyList<Guid> documentIds, Guid? userId, string? userName, DateTimeOffset? dueAt, CancellationToken ct)
    {
        var ids = documentIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) throw new InvalidOperationException("Selecione ao menos um prontuário para registrar a saída.");
        if (ids.Count > MaxItemsPerBatch) throw new InvalidOperationException($"Máximo de {MaxItemsPerBatch} prontuários por lote de saída.");
        if (string.IsNullOrWhiteSpace(carrierName)) throw new InvalidOperationException("Informe o nome ou a matrícula do responsável pelo transporte.");
        if (string.IsNullOrWhiteSpace(sectorName)) throw new InvalidOperationException("Selecione o setor destino da saída.");

        var carrier = Trunc(carrierName.Trim(), 200);
        var setor = Trunc(sectorName.Trim(), 200);
        var sectorDb = sectorId == Guid.Empty ? (Guid?)null : sectorId;
        var carrierIdDb = TrimTrunc(carrierId, 50);
        var reasonDb = TrimTrunc(reason, 500);
        var userDb = TrimTrunc(userName, 200);

        await using var db = await factory.OpenAsync(ct);

        // Valida os documentos do catálogo deste tenant antes de qualquer gravação.
        var docs = (await db.QueryAsync<CatalogRow>(new CommandDefinition("""
select d.id Id, coalesce(d.code,'SEM-CODIGO') Code, false IsOut
from ged.document d
where d.tenant_id=@t and d.reg_status='A' and d.deleted_at is null and d.id = any(@ids)
""", new { t = tenantId, ids }, cancellationToken: ct))).ToList();
        var byId = docs.ToDictionary(x => x.Id, x => x.Code);
        var rejected = new List<TransitRejectedItem>();
        foreach (var id in ids)
            if (!byId.ContainsKey(id))
                rejected.Add(new TransitRejectedItem("—", "Prontuário não encontrado neste ambiente."));
        if (byId.Count == 0) return new TransitCheckoutResult(Guid.Empty, string.Empty, 0, rejected);

        // RN01 — guarda-serviço: itens que já possuem saída em aberto são recusados individualmente.
        var openDocs = (await db.QueryAsync<Guid>(new CommandDefinition(
            "select document_id from ged.physical_loan where tenant_id=@t and reg_status='A' and status='OPEN' and document_id = any(@ids)",
            new { t = tenantId, ids = byId.Keys.ToList() }, cancellationToken: ct))).ToList();
        foreach (var openDoc in openDocs)
        {
            rejected.Add(new TransitRejectedItem(byId[openDoc], "Já possui saída em aberto neste momento."));
            byId.Remove(openDoc);
        }
        if (byId.Count == 0) return new TransitCheckoutResult(Guid.Empty, string.Empty, 0, rejected);

        // Escrita atômica: cabeçalho do lote + linhas de empréstimo + eventos de custódia em uma única transação.
        await using var tx = await db.BeginTransactionAsync(ct);
        var batchId = Guid.NewGuid();
        var batchNumber = "TRN-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        try
        {
            await db.ExecuteAsync(new CommandDefinition("""
insert into ged.physical_loan_batch(id,tenant_id,batch_number,destination_sector_id,destination_sector_name,carrier_name,carrier_id,reason,due_at,items_total,created_by,created_by_name,correlation_id)
values(@batchId,@t,@batchNumber,@sector,@sectorNome,@carrier,@carrierIdDb,@reasonDb,@dueAt,@total,@userIdDb,@userDb,gen_random_uuid()::text)
""", new
            {
                batchId, t = tenantId, batchNumber, sector = sectorDb, sectorNome = setor, carrier,
                carrierIdDb, reasonDb, dueAt, total = byId.Count, userIdDb = userId, userDb
            }, tx, cancellationToken: ct));

            var accepted = byId.ToList();
            var p = new DynamicParameters();
            p.Add("t", tenantId);
            p.Add("bid", batchId);
            p.Add("sid", sectorDb);
            p.Add("sname", setor);
            p.Add("car", carrier);
            p.Add("carid", carrierIdDb);
            p.Add("reason", reasonDb);
            p.Add("dueAt", dueAt);
            p.Add("userId", userId);
            p.Add("evDesc", $"Lote {batchNumber} → setor {setor}; responsável pelo transporte: {carrier}");
            p.Add("uid", userId);
            p.Add("uname", userDb);
            var loanRows = new List<string>();
            var eventRows = new List<string>();
            for (var i = 0; i < accepted.Count; i++)
            {
                var item = accepted[i];
                p.Add($"lid{i}", Guid.NewGuid());
                p.Add($"did{i}", item.Key);
                p.Add($"lnum{i}", batchNumber + "-" + item.Value);
                loanRows.Add($"(@lid{i},@t,@lnum{i},@bid,@did{i},@sid,@sname,@car,@carid,@car,@sname,@reason,@dueAt,@userId)");
                eventRows.Add($"(@t,'DOCUMENT',@did{i},'PHYSICAL_CHECKOUT','Saída registrada',@evDesc,@uid,@uname)");
            }
            await db.ExecuteAsync(new CommandDefinition(
                "insert into ged.physical_loan(id,tenant_id,loan_number,batch_id,document_id,sector_id,sector_name,carrier_name,carrier_id,requested_by_name,requested_by_department,reason,due_at,loaned_by) values " + string.Join(",", loanRows),
                p, tx, cancellationToken: ct));
            await db.ExecuteAsync(new CommandDefinition(
                "insert into ged.physical_custody_event(tenant_id,source_type,source_id,event_type,title,description,performed_by,performed_by_name) values " + string.Join(",", eventRows),
                p, tx, cancellationToken: ct));
            await db.ExecuteAsync(new CommandDefinition("""
insert into ged.physical_custody_event(tenant_id,source_type,source_id,event_type,title,description,performed_by,performed_by_name)
values(@t,'PHYSICAL_LOAN_BATCH',@bid,'BATCH_OPENED','Lote de saída aberto',@desc2,@uid,@uname)
""", new { t = tenantId, bid = batchId, desc2 = $"{accepted.Count} prontuário(s) para {setor}", uid = userId, uname = userDb }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return new TransitCheckoutResult(batchId, batchNumber, accepted.Count, rejected);
        }
        catch (NpgsqlException ex) when (ex.SqlState is "23503" or "23505")
        {
            await tx.RollbackAsync(ct);
            // Concorrência: outro usuário abriu o empréstimo entre a validação e a gravação (índice único parcial).
            throw new InvalidOperationException("Um ou mais prontuários selecionados já estavam em trânsito em outro momento. Revise a lista e tente novamente.", ex);
        }
    }

    public async Task<bool> CheckinAsync(Guid tenantId, Guid batchId, IReadOnlyList<Guid> loanIds, string? notes, Guid? userId, string? userName, CancellationToken ct)
    {
        var ids = loanIds.Where(x => x != Guid.Empty).Distinct().ToList();
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var batch = await db.QueryFirstOrDefaultAsync<StatusRow>(new CommandDefinition(
            "select status from ged.physical_loan_batch where tenant_id=@t and id=@bid and reg_status='A' for update",
            new { t = tenantId, bid = batchId }, tx, cancellationToken: ct));
        if (batch is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        if (batch.Status != "OPEN")
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException("Este lote já está encerrado; nenhuma devolução adicional pode ser registrada.");
        }
        if (ids.Count == 0)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException("Selecione ao menos um item para devolver.");
        }

        var notesDb = TrimTrunc(notes, 500);
        var affected = await db.ExecuteAsync(new CommandDefinition(
            "update ged.physical_loan set status='RETURNED', returned_by=@uid, returned_at=now(), return_notes=@notes where tenant_id=@t and batch_id=@bid and id = any(@ids) and status='OPEN'",
            new { t = tenantId, bid = batchId, uid = userId, notes = notesDb, ids }, tx, cancellationToken: ct));
        if (affected == 0)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException("Nenhum dos itens selecionados está em aberto neste lote.");
        }

        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.physical_custody_event(tenant_id,source_type,source_id,event_type,title,description,performed_by,performed_by_name)
select tenant_id,'DOCUMENT',document_id,'PHYSICAL_CHECKIN','Devolução registrada',coalesce(@notes,'Itens do lote devolvidos.'),@uid,@uname
from ged.physical_loan where tenant_id=@t and batch_id=@bid and id = any(@ids) and status='RETURNED'
""", new { t = tenantId, bid = batchId, notes = notesDb, uid = userId, uname = TrimTrunc(userName, 200), ids }, tx, cancellationToken: ct));

        await RecountAndMaybeCloseAsync(db, tx, tenantId, batchId, userId, userName, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<TransitLostResult> MarkLostAsync(Guid tenantId, Guid loanId, string? justification, Guid? userId, string? userName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(justification)) return new TransitLostResult(false, "Descreva o motivo do extravio antes de confirmar.", null);
        if (justification.Trim().Length < 5) return new TransitLostResult(false, "O motivo do extravio precisa de pelo menos 5 caracteres.", null);
        var just = Trunc(justification.Trim(), 500);
        var uname = TrimTrunc(userName, 200);

        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var loan = await db.QueryFirstOrDefaultAsync<LoanLockRow>(new CommandDefinition(
            "select id, status, batch_id as BatchId from ged.physical_loan where tenant_id=@t and id=@lid and reg_status='A' for update",
            new { t = tenantId, lid = loanId }, tx, cancellationToken: ct));
        if (loan is null)
        {
            await tx.RollbackAsync(ct);
            return new TransitLostResult(false, "Registro não encontrado neste ambiente.", null);
        }
        if (loan.Status != "OPEN")
        {
            await tx.RollbackAsync(ct);
            return new TransitLostResult(false, "Este registro já foi encerrado (devolvido ou extraviado).", null);
        }

        await db.ExecuteAsync(new CommandDefinition(
            "update ged.physical_loan set status='LOST', lost_reason=@just, returned_at=now() where tenant_id=@t and id=@lid and status='OPEN'",
            new { t = tenantId, lid = loanId, just }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.physical_custody_event(tenant_id,source_type,source_id,event_type,title,description,performed_by,performed_by_name)
select tenant_id,'DOCUMENT',document_id,'PHYSICAL_LOAN_LOST','Extravio registrado',@just,@uid,@uname
from ged.physical_loan where tenant_id=@t and id=@lid
""", new { t = tenantId, lid = loanId, just, uid = userId, uname }, tx, cancellationToken: ct));

        if (loan.BatchId.HasValue)
            await RecountAndMaybeCloseAsync(db, tx, tenantId, loan.BatchId.Value, userId, userName, ct);
        await tx.CommitAsync(ct);
        return new TransitLostResult(true, null, loan.BatchId);
    }

    public async Task<TransitBatchQuery> BatchesAsync(Guid tenantId, Guid? sectorId, string? statusFilter, string? q, CancellationToken ct)
    {
        var kind = (statusFilter ?? string.Empty).Trim().ToUpperInvariant();
        if (kind is not ("OPEN" or "OVERDUE" or "CLOSED")) kind = "";

        await using var db = await factory.OpenAsync(ct);
        var counts = await db.QueryFirstAsync<CountsRow>(new CommandDefinition("""
select
 (select count(*) from ged.physical_loan_batch b where b.tenant_id=@t and b.reg_status='A' and b.status='OPEN') as OpenBatches,
 (select count(*) from ged.physical_loan l where l.tenant_id=@t and l.reg_status='A' and l.batch_id is not null and l.status='OPEN') as OpenItems,
 (select count(*) from ged.physical_loan l where l.tenant_id=@t and l.reg_status='A' and l.batch_id is not null and l.status='OPEN' and l.due_at is not null and l.due_at < now()) as OverdueItems,
 (select count(*) from ged.physical_loan l where l.tenant_id=@t and l.reg_status='A' and l.batch_id is not null and l.status='RETURNED' and coalesce(l.returned_at,l.loaned_at) >= date_trunc('day',now())) as ReturnedToday
from (select 1) seed
""", new { t = tenantId }, cancellationToken: ct));

        var p = new DynamicParameters();
        p.Add("t", tenantId);
        p.Add("q", q?.Trim(), System.Data.DbType.String);
        var where = "b.tenant_id=@t and b.reg_status='A'";
        if (sectorId.HasValue && sectorId.Value != Guid.Empty)
        {
            where += " and b.destination_sector_id=@sectorId";
            p.Add("sectorId", sectorId.Value);
        }
        switch (kind)
        {
            case "OPEN": where += " and b.status='OPEN' and not (b.due_at is not null and b.due_at < now())"; break;
            case "OVERDUE": where += " and b.status='OPEN' and b.due_at is not null and b.due_at < now()"; break;
            case "CLOSED": where += " and b.status='CLOSED'"; break;
        }
        where += @" and (@q is null or b.batch_number ilike '%'||@q||'%' or b.destination_sector_name ilike '%'||@q||'%' or coalesce(b.carrier_name,'') ilike '%'||@q||'%')";

        var sql = $"""
select b.id Id,b.batch_number Number,b.destination_sector_name SectorName,b.carrier_name CarrierName,b.carrier_id CarrierId,
 b.items_total ItemsTotal,b.items_returned ItemsReturned,
 (select count(*) from ged.physical_loan l where l.batch_id=b.id and l.reg_status='A' and l.status='LOST') ItemsLost,
 case when b.status='OPEN' and b.due_at is not null and b.due_at < now() then 'OVERDUE' else b.status end Status,
 b.created_at CreatedAt,b.due_at DueAt,b.created_by_name CreatorName,
 (b.status='OPEN' and b.due_at is not null and b.due_at < now()) Overdue
from ged.physical_loan_batch b
where {where}
order by b.created_at desc limit 200
""";
        var rows = (await db.QueryAsync<BatchRow>(new CommandDefinition(sql, p, cancellationToken: ct))).ToList();
        return new TransitBatchQuery(
            new TransitCounts(counts.OpenBatches, counts.OpenItems, counts.OverdueItems, counts.ReturnedToday),
            rows.Select(MapBatch).ToList());
    }

    public async Task<TransitBatchDetails?> BatchDetailAsync(Guid tenantId, Guid batchId, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var batch = await db.QueryFirstOrDefaultAsync<BatchRow>(new CommandDefinition($"""
select b.id Id,b.batch_number Number,b.destination_sector_name SectorName,b.carrier_name CarrierName,b.carrier_id CarrierId,
 b.items_total ItemsTotal,b.items_returned ItemsReturned,
 (select count(*) from ged.physical_loan l where l.batch_id=b.id and l.reg_status='A' and l.status='LOST') ItemsLost,
 case when b.status='OPEN' and b.due_at is not null and b.due_at < now() then 'OVERDUE' else b.status end Status,
 b.created_at CreatedAt,b.due_at DueAt,b.created_by_name CreatorName,
 (b.status='OPEN' and b.due_at is not null and b.due_at < now()) Overdue
from ged.physical_loan_batch b
where b.tenant_id=@t and b.id=@bid and b.reg_status='A'
""", new { t = tenantId, bid = batchId }, cancellationToken: ct));
        if (batch is null) return null;

        var items = (await db.QueryAsync<LoanItemRow>(new CommandDefinition("""
select l.id Id,l.document_id DocumentId,coalesce(d.code,'—') DocCode,left(coalesce(d.title,'Sem título'),120) DocTitle,
 case when l.status='OPEN' and l.due_at is not null and l.due_at < now() then 'OVERDUE' else l.status end Status,
 l.carrier_name CarrierName,l.loaned_at LoanedAt,l.due_at DueAt,l.returned_at ReturnedAt,l.lost_reason LostReason
from ged.physical_loan l
join ged.physical_loan_batch b on b.id=l.batch_id
left join ged.document d on d.id=l.document_id
where l.tenant_id=@t and l.reg_status='A' and l.batch_id=@bid
order by l.loaned_at, l.loan_number
""", new { t = tenantId, bid = batchId }, cancellationToken: ct))).ToList();

        return new TransitBatchDetails(MapBatch(batch), items.Select(MapLoan).ToList());
    }

    public async Task<TransitDocumentHistory> DocumentHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var doc = await db.QueryFirstOrDefaultAsync<CatalogRow>(new CommandDefinition("""
select d.id Id, coalesce(d.code,'SEM-CODIGO') Code, left(coalesce(d.title,'Sem título'),120) Title,
 exists(select 1 from ged.physical_loan l where l.tenant_id=d.tenant_id and l.document_id=d.id and l.reg_status='A' and l.status='OPEN') IsOut
from ged.document d where d.tenant_id=@t and d.id=@doc
""", new { t = tenantId, doc = documentId }, cancellationToken: ct));
        var events = (await db.QueryAsync<EventRow>(new CommandDefinition("""
select occurred_at OccurredAt,event_type EventType,title Title,description Description,performed_by_name ActorName
from ged.physical_custody_event
where tenant_id=@t and reg_status='A' and source_type='DOCUMENT' and source_id=@doc
order by occurred_at desc limit 300
""", new { t = tenantId, doc = documentId }, cancellationToken: ct))).ToList();
        return new TransitDocumentHistory(
            doc is null ? null : new TransitCatalogItem(doc.Id, doc.Code, doc.Title, doc.IsOut),
            events.Select(e => new TransitCustodyRow(Off(e.OccurredAt), e.EventType, e.Title, e.Description, e.ActorName)).ToList());
    }

    /// <summary>Recalcula contadores do lote e encerra (CLOSED + evento) quando nenhum item permanece em OPEN.</summary>
    private static async Task RecountAndMaybeCloseAsync(System.Data.IDbConnection db, System.Data.IDbTransaction tx, Guid tenantId, Guid batchId, Guid? userId, string? userName, CancellationToken ct)
    {
        var uname = TrimTrunc(userName, 200);
        await db.ExecuteAsync(new CommandDefinition("""
update ged.physical_loan_batch b set
 items_returned=(select count(*) from ged.physical_loan l where l.batch_id=b.id and l.reg_status='A' and l.status='RETURNED'),
 status=case when (select count(*) from ged.physical_loan l where l.batch_id=b.id and l.reg_status='A' and l.status='OPEN')=0 then 'CLOSED' else b.status end,
 closed_at=case when (select count(*) from ged.physical_loan l where l.batch_id=b.id and l.reg_status='A' and l.status='OPEN')=0 then now() else b.closed_at end,
 closed_by=case when (select count(*) from ged.physical_loan l where l.batch_id=b.id and l.reg_status='A' and l.status='OPEN')=0 then @uid else b.closed_by end,
 closed_by_name=case when (select count(*) from ged.physical_loan l where l.batch_id=b.id and l.reg_status='A' and l.status='OPEN')=0 then @uname else b.closed_by_name end
where b.tenant_id=@t and b.id=@bid
""", new { t = tenantId, bid = batchId, uid = userId, uname }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.physical_custody_event(tenant_id,source_type,source_id,event_type,title,description,performed_by,performed_by_name)
select tenant_id,'PHYSICAL_LOAN_BATCH',id,'BATCH_CLOSED','Lote encerrado',null,@uid,@uname
from ged.physical_loan_batch where tenant_id=@t and id=@bid and status='CLOSED'
""", new { t = tenantId, bid = batchId, uid = userId, uname }, tx, cancellationToken: ct));
    }

    private static TransitBatchRow MapBatch(BatchRow x) => new(
        x.Id, x.Number, x.SectorName, x.CarrierName, x.CarrierId, x.ItemsTotal, x.ItemsReturned, x.ItemsLost,
        x.Status, Off(x.CreatedAt), x.DueAt.HasValue ? Off(x.DueAt.Value) : null, x.CreatorName, x.Overdue);

    private static TransitLoanRow MapLoan(LoanItemRow x) => new(
        x.Id, x.DocumentId, x.DocCode, x.DocTitle, x.Status, x.CarrierName,
        Off(x.LoanedAt), x.DueAt.HasValue ? Off(x.DueAt.Value) : null, x.ReturnedAt.HasValue ? Off(x.ReturnedAt.Value) : null, x.LostReason);

    private static DateTimeOffset Off(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string Trunc(string value, int max) => value.Length <= max ? value : value[..max];
    private static string? TrimTrunc(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return Trunc(v!, max);
    }

    private sealed class SectorRow { public Guid Id { get; set; } public string Nome { get; set; } = ""; public string? Sigla { get; set; } }
    private sealed class CatalogRow { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = ""; public bool IsOut { get; set; } }
    private sealed class StatusRow { public string Status { get; set; } = ""; }
    private sealed class LoanLockRow { public Guid Id { get; set; } public string Status { get; set; } = ""; public Guid? BatchId { get; set; } }
    private sealed class CountsRow { public long OpenBatches { get; set; } public long OpenItems { get; set; } public long OverdueItems { get; set; } public long ReturnedToday { get; set; } }
    private sealed class BatchRow { public Guid Id { get; set; } public string Number { get; set; } = ""; public string SectorName { get; set; } = ""; public string? CarrierName { get; set; } public string? CarrierId { get; set; } public int ItemsTotal { get; set; } public int ItemsReturned { get; set; } public int ItemsLost { get; set; } public string Status { get; set; } = ""; public DateTime CreatedAt { get; set; } public DateTime? DueAt { get; set; } public string? CreatorName { get; set; } public bool Overdue { get; set; } }
    private sealed class LoanItemRow { public Guid Id { get; set; } public Guid? DocumentId { get; set; } public string DocCode { get; set; } = ""; public string? DocTitle { get; set; } public string Status { get; set; } = ""; public string? CarrierName { get; set; } public DateTime LoanedAt { get; set; } public DateTime? DueAt { get; set; } public DateTime? ReturnedAt { get; set; } public string? LostReason { get; set; } }
    private sealed class EventRow { public DateTime OccurredAt { get; set; } public string EventType { get; set; } = ""; public string Title { get; set; } = ""; public string? Description { get; set; } public string? ActorName { get; set; } }
}
