using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.PhysicalArchive2;

namespace InovaGed.Infrastructure.PhysicalArchive2;

public sealed class PhysicalArchive2Service(IDbConnectionFactory factory) : IPhysicalArchive2Service
{
    public async Task<PhysicalArchiveDashboard> DashboardAsync(Guid t, CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);
        var tables = new[] { "physical_location", "physical_box", "physical_box_document", "physical_movement", "physical_inventory_session", "physical_inventory_item", "physical_loan", "physical_custody_event" };
        var active = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
            active[table] = await HasColumnAsync(db, "ged", table, "reg_status", ct) ? " and reg_status='A'" : string.Empty;
        var boxHasStatus = await HasColumnAsync(db, "ged", "physical_box", "status", ct);
        var boxHasHolder = await HasColumnAsync(db, "ged", "physical_box", "current_holder", ct);
        var boxHasLabel = await HasColumnAsync(db, "ged", "physical_box", "label_code", ct);
        var boxHasLocation = await HasColumnAsync(db, "ged", "physical_box", "location_id", ct);
        var sessionHasStatus = await HasColumnAsync(db, "ged", "physical_inventory_session", "status", ct);
        var loanHasStatus = await HasColumnAsync(db, "ged", "physical_loan", "status", ct);
        var loanHasDue = await HasColumnAsync(db, "ged", "physical_loan", "due_at", ct);
        var itemHasResult = await HasColumnAsync(db, "ged", "physical_inventory_item", "result", ct);
        var movementHasPerformed = await HasColumnAsync(db, "ged", "physical_movement", "performed_at", ct);
        var labelledPred = boxHasLabel ? " and label_code is not null" : " and false";
        var unlocatedPred = boxHasLocation ? " and location_id is null" : " and false";
        var loanedPred = boxHasStatus ? " and status='LOANED'" : boxHasHolder ? " and current_holder is not null" : " and false";
        var openInvPred = sessionHasStatus ? " and status='OPEN'" : string.Empty;
        var overduePred = loanHasDue
            ? (loanHasStatus ? " and status='OPEN' and due_at<now()" : " and due_at<now()")
            : " and false";
        var monthPred = movementHasPerformed ? " and performed_at>=date_trunc('month',now())" : string.Empty;
        var pendingPred = itemHasResult ? " and result in ('PENDING','MISSING','WRONG_LOCATION')" : string.Empty;
        var r=await db.QuerySingleAsync<DashboardRow>(new CommandDefinition($"""
select (select count(*) from ged.physical_box where tenant_id=@t{active["physical_box"]}) as "Boxes",
(select count(*) from ged.physical_box where tenant_id=@t{active["physical_box"]}{labelledPred}) as "LabelledBoxes",
(select count(*) from ged.physical_box where tenant_id=@t{active["physical_box"]}{unlocatedPred}) as "UnlocatedBoxes",
(select count(*) from ged.physical_box where tenant_id=@t{active["physical_box"]}{loanedPred}) as "LoanedBoxes",
(select count(*) from ged.physical_inventory_session where tenant_id=@t{active["physical_inventory_session"]}{openInvPred}) as "OpenInventories",
(select count(*) from ged.physical_loan where tenant_id=@t{active["physical_loan"]}{overduePred}) as "OverdueLoans",
(select count(*) from ged.physical_movement where tenant_id=@t{active["physical_movement"]}{monthPred}) as "MonthlyMovements",
(select count(*) from ged.physical_inventory_item where tenant_id=@t{active["physical_inventory_item"]}{pendingPred}) as "PendingChecks"
""",new{t},cancellationToken:ct));
        return new(r.Boxes,r.LabelledBoxes,r.UnlocatedBoxes,r.LoanedBoxes,r.OpenInventories,r.OverdueLoans,r.MonthlyMovements,r.PendingChecks);
    }
    private static async Task<bool> TableExistsAsync(System.Data.IDbConnection connection, string table, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass(@qualified) is not null", new { qualified = "ged." + table }, cancellationToken: ct));

    private static async Task<HashSet<string>> LoadColumnsAsync(System.Data.IDbConnection connection, string table, CancellationToken ct)
    {
        var columns = await connection.QueryAsync<string>(new CommandDefinition(
            "select column_name from information_schema.columns where table_schema='ged' and table_name=@table",
            new { table }, cancellationToken: ct));
        return columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> InventoryListSqlAsync(System.Data.IDbConnection db, CancellationToken ct)
    {
        var cols = await LoadColumnsAsync(db, "physical_inventory_session", ct);
        var code = cols.Contains("session_number") ? "s.session_number" : "left(s.id::text, 8)";
        var title = cols.Contains("title") ? "s.title" : "'Conferência de acervo'";
        var status = cols.Contains("status") ? "s.status" : "'OPEN'";
        var when = cols.Contains("started_at") ? "s.started_at" : cols.Contains("opened_at") ? "s.opened_at" : "now()";
        var notes = cols.Contains("notes") ? "s.notes" : "null::text";
        var reg = cols.Contains("reg_status") ? " and s.reg_status='A'" : "";
        var (join, locationName) = await LocationJoinAsync(db, cols, ct);
        return $"""
select s.id as Id, {code} as Code, {title} as Title, {status} as Status, {locationName} as Location, {when} as OccurredAt, {notes} as Detail
from ged.physical_inventory_session s{join}
where s.tenant_id=@t{reg}
order by {when} desc
""";
    }

    private async Task<(string Join, string Name)> LocationJoinAsync(System.Data.IDbConnection db, HashSet<string> sessionColumns, CancellationToken ct)
    {
        if (!sessionColumns.Contains("location_id") || !await TableExistsAsync(db, "physical_location", ct))
            return ("", "null::text");
        var locationColumns = await LoadColumnsAsync(db, "physical_location", ct);
        var name = locationColumns.Contains("name") ? "l.name" : locationColumns.Contains("unit_name") ? "l.unit_name" : locationColumns.Contains("location_code") ? "l.location_code" : "null::text";
        return (" left join ged.physical_location l on l.tenant_id=s.tenant_id and l.id=s.location_id", name);
    }

    private async Task<IReadOnlyList<PhysicalActivity>> ListInventoryItemsAsync(System.Data.IDbConnection db, Guid t, Guid id, CancellationToken ct)
    {
        if (!await TableExistsAsync(db, "physical_inventory_item", ct)) return Array.Empty<PhysicalActivity>();
        var cols = await LoadColumnsAsync(db, "physical_inventory_item", ct);
        var boxCols = await TableExistsAsync(db, "physical_box", ct) ? await LoadColumnsAsync(db, "physical_box", ct) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var when = cols.Contains("scanned_at") ? "coalesce(i.scanned_at, now())" : cols.Contains("checked_at") ? "coalesce(i.checked_at, now())" : "now()";
        var status = cols.Contains("result") ? "i.result" : "'PENDING'";
        var notes = cols.Contains("notes") ? "i.notes" : "null::text";
        var reg = cols.Contains("reg_status") ? " and i.reg_status='A'" : "";
        var join = cols.Contains("box_id") && boxCols.Count > 0 ? " left join ged.physical_box b on b.tenant_id=i.tenant_id and b.id=i.box_id" : "";
        var code = cols.Contains("scanned_code") ? "coalesce(i.scanned_code, '—')" : "'—'";
        var title = "'Caixa conferida'";
        if (join.Length > 0)
        {
            var boxCode = await ResolvePhysicalBoxCodeExpressionAsync(db, ct, "b");
            code = cols.Contains("scanned_code") ? $"coalesce({boxCode}, i.scanned_code, '—')" : $"coalesce({boxCode}, '—')";
            if (boxCols.Contains("title")) title = "coalesce(b.title, 'Caixa conferida')";
        }
        var found = cols.Contains("found_location_id") ? "i.found_location_id" : cols.Contains("observed_location_id") ? "i.observed_location_id" : null;
        var locationJoin = "";
        var location = "null::text";
        if (found is not null && await TableExistsAsync(db, "physical_location", ct))
        {
            var locationColumns = await LoadColumnsAsync(db, "physical_location", ct);
            location = locationColumns.Contains("name") ? "l.name" : locationColumns.Contains("location_code") ? "l.location_code" : "null::text";
            locationJoin = $" left join ged.physical_location l on l.tenant_id=i.tenant_id and l.id={found}";
        }
        var rows = await db.QueryAsync<ActivityRow>(new CommandDefinition($"""
select i.id as Id, {code} as Code, {title} as Title, {status} as Status, {location} as Location, {when} as OccurredAt, {notes} as Detail
from ged.physical_inventory_item i{join}{locationJoin}
where i.tenant_id=@t and i.session_id=@id{reg}
order by {when} desc
""", new { t, id }, cancellationToken: ct));
        return MapActivities(rows);
    }

    private static async Task<bool> HasColumnAsync(System.Data.IDbConnection connection,string schema,string table,string column,CancellationToken ct)
    {
        const string sql="select exists(select 1 from information_schema.columns where table_schema=@schema and table_name=@table and column_name=@column)";
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql,new{schema,table,column},cancellationToken:ct));
    }
    public async Task<IReadOnlyList<PhysicalOption>> BoxesAsync(Guid t,CancellationToken ct){await using var db=await factory.OpenAsync(ct);var code=await ResolvePhysicalBoxCodeExpressionAsync(db,ct);var rows=await db.QueryAsync<OptionRow>(new CommandDefinition($"select id as Id,{code} as Code,coalesce(title,{code}) as Name from ged.physical_box where tenant_id=@t and reg_status='A' order by {code}",new{t},cancellationToken:ct));return rows.Select(x=>new PhysicalOption(x.Id,x.Code,x.Name)).ToList();}
    public async Task<IReadOnlyList<PhysicalOption>> LocationsAsync(Guid t, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        if (!await TableExistsAsync(db, "physical_location", ct)) return Array.Empty<PhysicalOption>();
        var cols = await LoadColumnsAsync(db, "physical_location", ct);
        var code = cols.Contains("location_code") ? "location_code" : "id::text";
        var name = cols.Contains("name") ? "name" : cols.Contains("unit_name") ? "coalesce(unit_name, location_code, id::text)" : code;
        var reg = cols.Contains("reg_status") ? " and reg_status='A'" : "";
        var active = cols.Contains("is_active") ? " and is_active" : "";
        var rows = await db.QueryAsync<OptionRow>(new CommandDefinition(
            $"select id as Id, coalesce({code}, id::text) as Code, coalesce({name}, '') as Name from ged.physical_location where tenant_id=@t{reg}{active} order by 2",
            new { t }, cancellationToken: ct));
        return rows.Select(x => new PhysicalOption(x.Id, x.Code, x.Name)).ToList();
    }
    private async Task<IReadOnlyList<PhysicalOption>> Options(string sql,Guid t,CancellationToken ct){await using var db=await factory.OpenAsync(ct);var rows=await db.QueryAsync<OptionRow>(new CommandDefinition(sql,new{t},cancellationToken:ct));return rows.Select(x=>new PhysicalOption(x.Id,x.Code,x.Name)).ToList();}
    public async Task<IReadOnlyList<PhysicalActivity>> MovementsAsync(Guid t,CancellationToken ct){await using var db=await factory.OpenAsync(ct);var code=await ResolvePhysicalBoxCodeExpressionAsync(db,ct,"b");var rows=await db.QueryAsync<ActivityRow>(new CommandDefinition($"select m.id as Id,{code} as Code,m.movement_type as Title,m.movement_type as Status,l.name as Location,m.performed_at as OccurredAt,m.reason as Detail from ged.physical_movement m join ged.physical_box b on b.tenant_id=m.tenant_id and b.id=m.box_id left join ged.physical_location l on l.tenant_id=m.tenant_id and l.id=m.to_location_id where m.tenant_id=@t and m.reg_status='A' order by m.performed_at desc limit 300",new{t},cancellationToken:ct));return MapActivities(rows);}
    public async Task<IReadOnlyList<PhysicalActivity>> InventoriesAsync(Guid t, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        if (!await TableExistsAsync(db, "physical_inventory_session", ct)) return Array.Empty<PhysicalActivity>();
        var sql = await InventoryListSqlAsync(db, ct);
        var rows = await db.QueryAsync<ActivityRow>(new CommandDefinition(sql, new { t }, cancellationToken: ct));
        return MapActivities(rows);
    }
    public async Task<IReadOnlyList<PhysicalActivity>> LoansAsync(Guid t,CancellationToken ct){await using var db=await factory.OpenAsync(ct);var code=await ResolvePhysicalBoxCodeExpressionAsync(db,ct,"b");var rows=await db.QueryAsync<ActivityRow>(new CommandDefinition($"select p.id as Id,p.loan_number as Code,p.requested_by_name as Title,case when p.status='OPEN' and p.due_at<now() then 'OVERDUE' else p.status end as Status,{code} as Location,p.loaned_at as OccurredAt,p.reason as Detail from ged.physical_loan p left join ged.physical_box b on b.tenant_id=p.tenant_id and b.id=p.box_id where p.tenant_id=@t and p.reg_status='A' order by p.loaned_at desc",new{t},cancellationToken:ct));return MapActivities(rows);}
    public Task<IReadOnlyList<PhysicalActivity>> CustodyAsync(Guid t,Guid boxId,CancellationToken ct)=>Activities("select id as Id,event_type as Code,title as Title,event_type as Status,source_type as Location,occurred_at as OccurredAt,concat_ws(' · ',description,correlation_id) as Detail from ged.physical_custody_event where tenant_id=@t and source_type='BOX' and source_id=@boxId and reg_status='A' order by occurred_at desc",t,ct,new{boxId});
    private async Task<IReadOnlyList<PhysicalActivity>> Activities(string sql,Guid t,CancellationToken ct,object? extra=null){await using var db=await factory.OpenAsync(ct);var p=new DynamicParameters(extra);p.Add("t",t);var rows=await db.QueryAsync<ActivityRow>(new CommandDefinition(sql,p,cancellationToken:ct));return rows.Select(x=>new PhysicalActivity(x.Id,x.Code,x.Title,x.Status,x.Location,AsOffset(x.OccurredAt),x.Detail)).ToList();}
    private static IReadOnlyList<PhysicalActivity> MapActivities(IEnumerable<ActivityRow> rows)=>rows.Select(x=>new PhysicalActivity(x.Id,x.Code,x.Title,x.Status,x.Location,AsOffset(x.OccurredAt),x.Detail)).ToList();
    private static async Task<string> ResolvePhysicalBoxCodeExpressionAsync(System.Data.IDbConnection connection,CancellationToken ct,string? alias=null)
    {
        var column=await connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name='physical_box' and column_name=any(array['box_code','box_no','code']) order by array_position(array['box_code','box_no','code'],column_name) limit 1",cancellationToken:ct));
        var prefix=string.IsNullOrWhiteSpace(alias)?string.Empty:alias+".";
        return column is "box_code" or "box_no" or "code" ? prefix+column : prefix+"id::text";
    }
    public async Task<PhysicalInventoryDetails?> InventoryAsync(Guid t, Guid id, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        if (!await TableExistsAsync(db, "physical_inventory_session", ct)) return null;
        var cols = await LoadColumnsAsync(db, "physical_inventory_session", ct);
        var number = cols.Contains("session_number") ? "s.session_number" : "left(s.id::text, 8)";
        var title = cols.Contains("title") ? "s.title" : "'Conferência de acervo'";
        var status = cols.Contains("status") ? "s.status" : "'OPEN'";
        var when = cols.Contains("started_at") ? "s.started_at" : cols.Contains("opened_at") ? "s.opened_at" : "now()";
        var locationId = cols.Contains("location_id") ? "s.location_id" : "null::uuid";
        var reg = cols.Contains("reg_status") ? " and s.reg_status='A'" : "";
        var (join, locationName) = await LocationJoinAsync(db, cols, ct);
        var session = await db.QuerySingleOrDefaultAsync<InventoryRow>(new CommandDefinition($"""
select s.id as Id, {number} as SessionNumber, {title} as Title, {status} as Status, {locationId} as LocationId, {locationName} as Location, {when} as StartedAt
from ged.physical_inventory_session s{join}
where s.tenant_id=@t and s.id=@id{reg}
""", new { t, id }, cancellationToken: ct));
        if (session is null) return null;
        var items = await ListInventoryItemsAsync(db, t, id, ct);
        return new(session.Id, session.SessionNumber, session.Title, session.Status, session.LocationId, session.Location, AsOffset(session.StartedAt), items);
    }

    public async Task<Guid> StartInventoryAsync(Guid t, Guid? locationId, string title, Guid? userId, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        if (!await TableExistsAsync(db, "physical_inventory_session", ct))
            throw new InvalidOperationException("A estrutura de inventário físico ainda não está disponível nesta instalação.");
        var cols = await LoadColumnsAsync(db, "physical_inventory_session", ct);
        var id = Guid.NewGuid();
        var fields = new List<string> { "id", "tenant_id" };
        var values = new List<string> { "@id", "@t" };
        if (cols.Contains("session_number")) { fields.Add("session_number"); values.Add("'INV-'||to_char(now(),'YYYYMMDD-HH24MISS')"); }
        if (cols.Contains("title")) { fields.Add("title"); values.Add("@title"); }
        if (cols.Contains("location_id")) { fields.Add("location_id"); values.Add("@locationId"); }
        if (cols.Contains("started_by")) { fields.Add("started_by"); values.Add("@userId"); }
        else if (cols.Contains("opened_by"))
        {
            if (userId is null) throw new InvalidOperationException("A sessão antiga exige o usuário que abre o inventário.");
            fields.Add("opened_by");
            values.Add("@userId");
        }
        if (cols.Contains("status")) { fields.Add("status"); values.Add("'OPEN'"); }
        await db.ExecuteAsync(new CommandDefinition(
            $"insert into ged.physical_inventory_session({string.Join(',', fields)}) values ({string.Join(',', values)})",
            new { id, t, title = title.Trim(), locationId, userId }, cancellationToken: ct));
        return id;
    }

    public async Task ScanAsync(Guid t, Guid sessionId, string code, Guid? foundLocationId, Guid? userId, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var itemCols = await LoadColumnsAsync(db, "physical_inventory_item", ct);
        var boxCols = await LoadColumnsAsync(db, "physical_box", ct);
        var boxCode = await ResolvePhysicalBoxCodeExpressionAsync(db, ct);
        var labelMatch = boxCols.Contains("label_code") ? " or upper(coalesce(label_code,''))=upper(@code)" : "";
        var boxReg = boxCols.Contains("reg_status") ? " and reg_status='A'" : "";
        var locationSelect = boxCols.Contains("location_id") ? "location_id" : "null::uuid as location_id";
        var foundCol = itemCols.Contains("found_location_id") ? "found_location_id" : itemCols.Contains("observed_location_id") ? "observed_location_id" : null;
        var byCol = itemCols.Contains("scanned_by") ? "scanned_by" : itemCols.Contains("checked_by") ? "checked_by" : null;
        var atCol = itemCols.Contains("scanned_at") ? "scanned_at" : itemCols.Contains("checked_at") ? "checked_at" : null;
        var fields = new List<string> { "tenant_id", "session_id" };
        var select = new List<string> { "@t", "@sessionId" };
        if (itemCols.Contains("box_id")) { fields.Add("box_id"); select.Add("b.id"); }
        if (itemCols.Contains("scanned_code")) { fields.Add("scanned_code"); select.Add("@code"); }
        if (itemCols.Contains("expected_location_id")) { fields.Add("expected_location_id"); select.Add("b.location_id"); }
        if (foundCol is not null) { fields.Add(foundCol); select.Add("@foundLocationId"); }
        if (itemCols.Contains("result"))
        {
            fields.Add("result");
            var comparison = boxCols.Contains("location_id")
                ? "when b.id is null then 'UNEXPECTED' when b.location_id is not distinct from @foundLocationId then 'FOUND' else 'WRONG_LOCATION'"
                : "when b.id is null then 'UNEXPECTED' else 'FOUND'";
            select.Add($"case {comparison} end");
        }
        if (byCol is not null) { fields.Add(byCol); select.Add("@userId"); }
        if (atCol is not null) { fields.Add(atCol); select.Add("now()"); }
        var sql = $"""
insert into ged.physical_inventory_item({string.Join(',', fields)})
select {string.Join(',', select)}
from (select 1) seed
left join lateral (
    select id, {locationSelect}
    from ged.physical_box
    where tenant_id=@t{boxReg} and (upper({boxCode})=upper(@code){labelMatch})
    limit 1
) b on true
""";
        await db.ExecuteAsync(new CommandDefinition(sql, new { t, sessionId, code = code.Trim(), foundLocationId, userId }, cancellationToken: ct));
    }

    public async Task CloseInventoryAsync(Guid t, Guid sessionId, Guid? userId, string? notes, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var cols = await LoadColumnsAsync(db, "physical_inventory_session", ct);
        var sets = new List<string>();
        if (cols.Contains("status")) sets.Add("status='CLOSED'");
        if (cols.Contains("closed_by")) sets.Add("closed_by=@userId");
        if (cols.Contains("closed_at")) sets.Add("closed_at=now()");
        if (cols.Contains("notes")) sets.Add("notes=@notes");
        if (sets.Count == 0) throw new InvalidOperationException("A sessão de inventário não possui colunas de encerramento.");
        var whereStatus = cols.Contains("status") ? " and status='OPEN'" : "";
        await db.ExecuteAsync(new CommandDefinition(
            $"update ged.physical_inventory_session set {string.Join(", ", sets)} where tenant_id=@t and id=@sessionId{whereStatus}",
            new { t, sessionId, userId, notes }, cancellationToken: ct));
    }
    public async Task MoveAsync(Guid t,Guid boxId,Guid toLocationId,string type,string? reason,Guid? userId,string? userName,CancellationToken ct){await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);var from=await db.ExecuteScalarAsync<Guid?>(new CommandDefinition("select location_id from ged.physical_box where tenant_id=@t and id=@boxId and reg_status='A' for update",new{t,boxId},tx,cancellationToken:ct));await db.ExecuteAsync(new CommandDefinition("update ged.physical_box set location_id=@toLocationId,updated_at=now() where tenant_id=@t and id=@boxId;insert into ged.physical_movement(tenant_id,box_id,from_location_id,to_location_id,movement_type,reason,performed_by,performed_by_name) values(@t,@boxId,@from,@toLocationId,@type,@reason,@userId,@userName);insert into ged.physical_custody_event(tenant_id,source_type,source_id,event_type,title,description,performed_by,performed_by_name,correlation_id) values(@t,'BOX',@boxId,'PHYSICAL_BOX_MOVED','Caixa movimentada',@reason,@userId,@userName,gen_random_uuid()::text)",new{t,boxId,from,toLocationId,type,reason,userId,userName},tx,cancellationToken:ct));await tx.CommitAsync(ct);}
    public async Task<Guid> LoanAsync(Guid t,Guid boxId,string requester,string? department,string? reason,DateTimeOffset? dueAt,Guid? userId,CancellationToken ct){await using var db=await factory.OpenAsync(ct);var id=Guid.NewGuid();await db.ExecuteAsync(new CommandDefinition("insert into ged.physical_loan(id,tenant_id,loan_number,box_id,requested_by_name,requested_by_department,reason,loaned_by,due_at) values(@id,@t,'EMP-'||to_char(now(),'YYYYMMDD-HH24MISS'),@boxId,@requester,@department,@reason,@userId,@dueAt);update ged.physical_box set status='LOANED',current_holder=@requester,updated_at=now() where tenant_id=@t and id=@boxId;insert into ged.physical_custody_event(tenant_id,source_type,source_id,event_type,title,description,performed_by) values(@t,'BOX',@boxId,'PHYSICAL_LOAN_CREATED','Empréstimo registrado',@reason,@userId)",new{id,t,boxId,requester=requester.Trim(),department,reason,userId,dueAt},cancellationToken:ct));return id;}
    public async Task ReturnLoanAsync(Guid t,Guid loanId,string? notes,Guid? userId,CancellationToken ct){await using var db=await factory.OpenAsync(ct);await db.ExecuteAsync(new CommandDefinition("update ged.physical_loan set status='RETURNED',returned_by=@userId,returned_at=now(),return_notes=@notes where tenant_id=@t and id=@loanId and status='OPEN';update ged.physical_box b set status='ACTIVE',current_holder=null,updated_at=now() from ged.physical_loan p where p.tenant_id=@t and p.id=@loanId and b.tenant_id=p.tenant_id and b.id=p.box_id;insert into ged.physical_custody_event(tenant_id,source_type,source_id,event_type,title,description,performed_by) select @t,'BOX',box_id,'PHYSICAL_LOAN_RETURNED','Caixa devolvida',@notes,@userId from ged.physical_loan where tenant_id=@t and id=@loanId",new{t,loanId,notes,userId},cancellationToken:ct));}
    private static DateTimeOffset AsOffset(DateTime value)=>new(DateTime.SpecifyKind(value,DateTimeKind.Utc));
    private sealed class DashboardRow{public long Boxes{get;set;}public long LabelledBoxes{get;set;}public long UnlocatedBoxes{get;set;}public long LoanedBoxes{get;set;}public long OpenInventories{get;set;}public long OverdueLoans{get;set;}public long MonthlyMovements{get;set;}public long PendingChecks{get;set;}}
    private sealed class OptionRow{public Guid Id{get;set;}public string Code{get;set;}="";public string Name{get;set;}="";}
    private sealed class ActivityRow{public Guid Id{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public string Status{get;set;}="";public string? Location{get;set;}public DateTime OccurredAt{get;set;}public string? Detail{get;set;}}
    private sealed class InventoryRow{public Guid Id{get;set;}public string SessionNumber{get;set;}="";public string Title{get;set;}="";public string Status{get;set;}="";public Guid? LocationId{get;set;}public string? Location{get;set;}public DateTime StartedAt{get;set;}}
}
