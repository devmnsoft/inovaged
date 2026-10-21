using Dapper;
using InovaGed.Application.Billing;
using InovaGed.Application.HospitalBilling;
using InovaGed.Application.Tests.Ged;
using InovaGed.Infrastructure.Billing;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.HospitalBilling;
using Npgsql;

namespace InovaGed.Application.Tests;

public sealed class BillingOperationalPostgresTests
{
    [LocalPgFact]
    public async Task Old_records_pagination_filters_and_tenant_isolation_use_real_migrations()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Conn.ExecuteAsync("""
insert into ged.billing_document_extraction(tenant_id,document_id,supplier_name,gross_amount,created_at)
select @tenant,gen_random_uuid(),case when n%2=0 then 'A' else 'B' end,10,now()-n*interval '1 minute'
from generate_series(1,350) n
""", new { tenant = f.Tenant });
        var old = await f.Conn.ExecuteScalarAsync<Guid>("select id from ged.billing_document_extraction order by created_at limit 1");
        Assert.NotNull(await f.Queries.GetAsync(f.Tenant, old, default));
        Assert.Null(await f.Queries.GetAsync(Guid.NewGuid(), old, default));
        var first = await f.Queries.DashboardAsync(f.Tenant, new BillingFilter(Supplier: "A", PageSize: 100), default);
        var second = await f.Queries.DashboardAsync(f.Tenant, new BillingFilter(Supplier: "A", Page: 2, PageSize: 100), default);
        Assert.Equal(350,(await f.Queries.ExportAsync(f.Tenant,new BillingFilter(),default)).Count);
        Assert.Equal(175,(await f.Queries.ExportAsync(f.Tenant,new BillingFilter(Supplier: "A"),default)).Count);
        Assert.Equal(175, first.Kpis.Extracted);
        Assert.Equal(1750, first.Kpis.TotalAmount);
        Assert.Equal(175, first.Kpis.WithoutOcr);
        Assert.Equal(100, first.Rows.Count);
        Assert.Equal(75, second.Rows.Count);
        Assert.Empty(first.Rows.Select(x=>x.Id).Intersect(second.Rows.Select(x=>x.Id)));
    }

    [LocalPgFact]
    public async Task Reextraction_preserves_review_and_requires_current_revision_and_audits_same_status_edits()
    {
        await using var f = await Fixture.CreateAsync();
        var e = f.Extraction();
        await f.Commands.SaveExtractionAsync(f.Tenant, e, default);
        var stored = await f.ByDocumentAsync(e.DocumentId);
        Assert.True(await f.Commands.ReviewAsync(f.Tenant, stored.Id, f.User, Review(stored), default));
        var approved = (await f.Queries.GetAsync(f.Tenant, stored.Id, default))!;
        e.SupplierName = "Novo fornecedor OCR"; e.GrossAmount = 120; e.NetAmount = 110; e.DocumentVersionId = Guid.NewGuid();
        await f.Commands.SaveExtractionAsync(f.Tenant, e, default);
        var pending = (await f.Queries.GetAsync(f.Tenant, stored.Id, default))!;
        Assert.Equal("Fornecedor", pending.SupplierName);
        Assert.Equal(100, pending.GrossAmount);
        Assert.Equal("APPROVED", pending.ExtractionStatus);
        Assert.NotNull(pending.PendingCandidateJson);
        Assert.False(await f.Commands.ReviewAsync(f.Tenant, stored.Id, f.User, Review(approved), default));
        var review = Review(pending); review.UseCandidate = true; review.SupplierName = e.SupplierName; review.GrossAmount = 120; review.NetAmount = 110;
        Assert.True(await f.Commands.ReviewAsync(f.Tenant, stored.Id, f.User, review, default));
        var accepted = (await f.Queries.GetAsync(f.Tenant, stored.Id, default))!;
        Assert.Equal("Novo fornecedor OCR", accepted.SupplierName);
        Assert.Equal(e.DocumentVersionId, accepted.DocumentVersionId);
        Assert.Null(accepted.PendingCandidateJson);
        Assert.False(await f.Commands.ReviewAsync(Guid.NewGuid(), stored.Id, f.User, Review(accepted), default));
        var history = await f.Operations.GetAsync(f.Tenant, stored.Id, default);
        Assert.Equal(2, history.History.Count);
        Assert.All(history.History, x => Assert.NotNull(x.PreviousSnapshotJson));
        Assert.Equal(2, await f.Conn.ExecuteScalarAsync<int>("select count(*) from ged.billing_extraction_candidate"));
        // Reapply the migration to verify additive, repeatable deployment.
        await f.ApplyAsync("2026_11_09_billing_operational_integrity.sql");
        Assert.Equal(2, (await f.Operations.GetAsync(f.Tenant, stored.Id, default)).History.Count);
    }

    [LocalPgFact]
    public async Task Unreviewed_supplier_updates_and_duplicates_require_explicit_confirmation()
    {
        await using var f = await Fixture.CreateAsync();
        var a = f.Extraction(); await f.Commands.SaveExtractionAsync(f.Tenant,a,default);
        a.SupplierName="Fornecedor corrigido"; await f.Commands.SaveExtractionAsync(f.Tenant,a,default);
        Assert.Equal(a.SupplierName,(await f.ByDocumentAsync(a.DocumentId)).SupplierName);
        var b = f.Extraction(); await f.Commands.SaveExtractionAsync(f.Tenant,b,default);
        var row=await f.ByDocumentAsync(a.DocumentId);
        Assert.Equal(1,row.DuplicateCount);
        await f.Operations.SaveDuplicatePolicyAsync(f.Tenant,f.User,new BillingDuplicatePolicy{CompareAmount=true},default);
        b.GrossAmount=200;await f.Commands.SaveExtractionAsync(f.Tenant,b,default);
        Assert.Equal(0,(await f.ByDocumentAsync(a.DocumentId)).DuplicateCount);
        await f.Operations.SaveDuplicatePolicyAsync(f.Tenant,f.User,new BillingDuplicatePolicy(),default);

        var review=Review(row);
        Assert.False(await f.Commands.ReviewAsync(f.Tenant,row.Id,f.User,review,default));
        review.ConfirmDuplicate=true;
        Assert.True(await f.Commands.ReviewAsync(f.Tenant,row.Id,f.User,review,default));
    }

    [LocalPgFact]
    public async Task Concurrent_reviewers_cannot_overwrite_each_other()
    {
        await using var f = await Fixture.CreateAsync();
        var e=f.Extraction(); await f.Commands.SaveExtractionAsync(f.Tenant,e,default);
        var row=await f.ByDocumentAsync(e.DocumentId);
        var results=await Task.WhenAll(f.Commands.ReviewAsync(f.Tenant,row.Id,f.User,Review(row),default), f.Commands.ReviewAsync(f.Tenant,row.Id,f.User,Review(row),default));
        Assert.Single(results.Where(x=>x));
        Assert.Single((await f.Operations.GetAsync(f.Tenant,row.Id,default)).History);
    }

    [LocalPgFact]
    public async Task Installments_partial_settlement_idempotency_reconciliation_and_overpayment()
    {
        await using var f=await Fixture.CreateAsync();
        var e=f.Extraction();await f.Commands.SaveExtractionAsync(f.Tenant,e,default);
        var row=await f.ByDocumentAsync(e.DocumentId);
        var installment=new BillingInstallmentInput { ExtractionId=row.Id,Amount=90,Sequence=1,Direction="PAYABLE",DueDate=DateTime.Today };
        Assert.False(await f.Operations.AddInstallmentAsync(f.Tenant,f.User,installment,default));
        Assert.True(await f.Commands.ReviewAsync(f.Tenant,row.Id,f.User,Review(row),default));
        Assert.True(await f.Operations.AddInstallmentAsync(f.Tenant,f.User,installment,default));
        Assert.False(await f.Operations.AddInstallmentAsync(f.Tenant,f.User,installment,default));
        Assert.False(await f.Operations.AddInstallmentAsync(f.Tenant,f.User,new(){ ExtractionId=row.Id,Amount=1,Sequence=2,DueDate=DateTime.Today },default));
        var ledger=await f.Operations.GetAsync(f.Tenant,row.Id,default);
        var settlement=new BillingSettlementInput { ExtractionId=row.Id,InstallmentId=ledger.Installments.Single().Id,Amount=30,SettledOn=DateTime.Today,Reference="Comprovante 1",IdempotencyKey=Guid.NewGuid() };
        Assert.True(await f.Operations.SettleAsync(f.Tenant,f.User,settlement,default));
        Assert.True(await f.Operations.SettleAsync(f.Tenant,f.User,settlement,default));
        settlement.Amount=31;
        Assert.False(await f.Operations.SettleAsync(f.Tenant,f.User,settlement,default));
        settlement.IdempotencyKey=Guid.NewGuid();settlement.Amount=70;
        Assert.False(await f.Operations.SettleAsync(f.Tenant,f.User,settlement,default));
        ledger=await f.Operations.GetAsync(f.Tenant,row.Id,default);
        Assert.Equal(60,ledger.Installments.Single().Balance);
        Assert.Equal("Parcial",ledger.Installments.Single().Status);
        Assert.Single(ledger.Settlements);
        Assert.False(await f.Operations.ReconcileAsync(Guid.NewGuid(),f.User,ledger.Settlements[0].Id,default));
        Assert.True(await f.Operations.ReconcileAsync(f.Tenant,f.User,ledger.Settlements[0].Id,default));
        settlement.Amount=60;
        Assert.False(await f.Operations.SettleAsync(Guid.NewGuid(),f.User,settlement,default));
        Assert.True(await f.Operations.SettleAsync(f.Tenant,f.User,settlement,default));
        Assert.Equal("Liquidado",(await f.Operations.GetAsync(f.Tenant,row.Id,default)).Installments.Single().Status);
    }

    [LocalPgFact]
    public async Task Hospital_review_preserves_patient_mask_and_validates_concurrency_and_recovery_balance()
    {
        await using var f=await Fixture.CreateAsync();
        var id=await f.Conn.ExecuteScalarAsync<Guid>("""
insert into ged.hospital_billing_document(tenant_id,document_id,patient_name,presented_amount,has_ocr)
values(@tenant,gen_random_uuid(),'Maria Silva',100,true) returning id
""",new {tenant=f.Tenant});
        var q=new HospitalBillingQueries(f.Factory);
        var row=(await q.GetAsync(f.Tenant,id,default))!;
        Assert.DoesNotContain("Maria",row.MaskedPatient);
        var review=new HospitalBillingReviewRequest { Id=id,Revision=row.Revision,Status="DENIED",ApprovedAmount=70,DeniedAmount=30,RecoveredAmount=10,AppealDueDate=new DateOnly(2026,11,1),DenialReason="Falta de comprovante",Notes="Conferência efetuada",AppealProtocol="P-123",AppealResponse="Recuperação parcial" };
        Assert.True(await q.ReviewAsync(f.Tenant,f.User,review,default));
        Assert.False(await q.ReviewAsync(f.Tenant,f.User,review,default));
        review.Revision++;
        review.RecoveredAmount=31;
        Assert.False(await q.ReviewAsync(f.Tenant,f.User,review,default));
        var saved=(await q.GetDetailsAsync(f.Tenant,id,default))!;
        Assert.Equal(10,saved.Document.RecoveredAmount);
        Assert.Equal("P-123",saved.Document.AppealProtocol);
        Assert.Single(saved.History);
        Assert.Null(await q.GetAsync(Guid.NewGuid(),id,default));
    }

    private static BillingReviewInput Review(BillingExtractionDto e)=>new()
    {
        Revision=e.Revision,SupplierName=e.SupplierName,SupplierDocument=e.SupplierDocument,InvoiceNumber=e.InvoiceNumber,
        GrossAmount=e.GrossAmount,NetAmount=e.NetAmount,Notes="Conferência dos dados fiscais"
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Tenant { get; }=Guid.NewGuid();
        public Guid User { get; }=Guid.NewGuid();
        public NpgsqlConnection Conn { get; }
        public NpgsqlConnectionFactory Factory { get; }
        public BillingQueries Queries { get; }
        public BillingCommands Commands { get; }
        public BillingOperations Operations { get; }
        private readonly string _database;
        private Fixture(string database,NpgsqlConnection conn,NpgsqlConnectionFactory factory)
        { _database=database;Conn=conn;Factory=factory;Queries=new(factory);Commands=new(factory);Operations=new(factory); }
        public BillingExtractionDto Extraction()=>new(){ DocumentId=Guid.NewGuid(),DocumentVersionId=Guid.NewGuid(),SupplierName="Fornecedor",SupplierDocument="04.252.011/0001-10",InvoiceNumber="123",DocumentKind="INVOICE",GrossAmount=100,NetAmount=90,Confidence=80,ExtractionStatus="PENDING_REVIEW" };
        public async Task<BillingExtractionDto> ByDocumentAsync(Guid id)
        { var key=await Conn.ExecuteScalarAsync<Guid>("select id from ged.billing_document_extraction where document_id=@id",new{id});return (await Queries.GetAsync(Tenant,key,default))!; }
        public static async Task<Fixture> CreateAsync()
        {
            var name="ig_billing_"+Guid.NewGuid().ToString("N");
            await using var admin=new NpgsqlConnection(LocalPg.AdminDsn);await admin.OpenAsync();
            await admin.ExecuteAsync($"create database {name}");
            var dsn=new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn){Database=name,Pooling=false}.ConnectionString;
            var conn=new NpgsqlConnection(dsn);await conn.OpenAsync();
            var f=new Fixture(name,conn,new(dsn));
            await conn.ExecuteAsync("""
create schema ged;
create table ged.document(id uuid,tenant_id uuid,title text,code text);
create table ged.app_user(id uuid,tenant_id uuid,name text,is_active bool default true,reg_status text default 'A');
create table ged.document_search(document_id uuid,tenant_id uuid,ocr_text text);
""");
            foreach(var file in new[]{"2026_08_11_billing_extraction.sql","2026_08_11_billing_review_governance.sql","2026_08_13_hospital_billing_smart_search.sql","2026_08_14_hospital_billing_review_history.sql","2026_08_17_hospital_billing_workflow_audit.sql","2026_11_09_billing_operational_integrity.sql"}) await f.ApplyAsync(file);
            return f;
        }
        public async Task ApplyAsync(string name)
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);
            while(root is not null&&!File.Exists(Path.Combine(root.FullName,"InovaGed.sln")))root=root.Parent;
            await Conn.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database","migrations",name)));
        }
        public async ValueTask DisposeAsync()
        {
            await Conn.DisposeAsync();
            await using var admin=new NpgsqlConnection(LocalPg.AdminDsn);await admin.OpenAsync();
            await admin.ExecuteAsync($"drop database {_database} with (force)");
        }
    }
}
