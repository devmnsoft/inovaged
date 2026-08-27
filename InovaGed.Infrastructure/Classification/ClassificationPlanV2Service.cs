using System.Text.Json;
using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Classification;

public sealed class ClassificationPlanV2Service : IClassificationPlanService, IRetentionRuleV2Service, IClassificationVersionService
{
    private readonly IDbConnectionFactory _db;
    public ClassificationPlanV2Service(IDbConnectionFactory db) => _db = db;

    public async Task<ClassificationPlanDashboard> GetDashboardAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = """
select count(*)::int as Classes,
 count(*) filter(where r.id is null)::int as ClassesWithoutRule,
 count(*) filter(where r.review_status='DRAFT')::int as RulesInReview,
 coalesce((select max(version_number) from ged.classification_plan_version_v2 where tenant_id=@tenantId and status='PUBLISHED' and reg_status='A'),0)::int as PublishedVersion,
 coalesce((select count(*) from ged.classification_change_request where tenant_id=@tenantId and status='OPEN' and reg_status='A'),0)::int as PendingChanges,
 count(*) filter(where r.final_destination='GUARDA_PERMANENTE')::int as PermanentDestinations
from ged.classification_node n left join ged.retention_rule_v2 r on r.tenant_id=n.tenant_id and r.classification_node_id=n.id and r.reg_status='A'
where n.tenant_id=@tenantId and n.reg_status='A';
""";
        await using var c = await _db.OpenAsync(ct);
        var row = await c.QuerySingleAsync<DashboardDbRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        return new(row.Classes,row.ClassesWithoutRule,row.RulesInReview,row.PublishedVersion,row.PendingChanges,row.PermanentDestinations);
    }

    public async Task<IReadOnlyList<ClassificationTreeNode>> GetTreeAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = """select n.id,n.parent_id as ParentId,n.code,n.title,n.activity_type as ActivityType,n.review_status as ReviewStatus,n.is_active as IsActive,(r.id is not null) as HasRetentionRule from ged.classification_node n left join ged.retention_rule_v2 r on r.tenant_id=n.tenant_id and r.classification_node_id=n.id and r.reg_status='A' where n.tenant_id=@tenantId and n.reg_status='A' order by n.display_order,n.code""";
        await using var c = await _db.OpenAsync(ct);
        var rows = await c.QueryAsync<NodeDbRow>(new CommandDefinition(sql,new {tenantId},cancellationToken:ct));
        return rows.Select(x => new ClassificationTreeNode(x.Id,x.ParentId,x.Code,x.Title,x.ActivityType,x.ReviewStatus,x.IsActive,x.HasRetentionRule)).ToList();
    }

    public async Task<ClassificationNodeDetails?> GetNodeAsync(Guid tenantId, Guid nodeId, CancellationToken ct)
    {
        const string sql="""select id,parent_id as ParentId,code,title,description,activity_type as ActivityType,document_function as DocumentFunction,normative_source as NormativeSource,keywords,display_order as DisplayOrder,review_status as ReviewStatus,is_active as IsActive from ged.classification_node where tenant_id=@tenantId and id=@nodeId and reg_status='A'""";
        await using var c=await _db.OpenAsync(ct); var x=await c.QuerySingleOrDefaultAsync<NodeDbRow>(new CommandDefinition(sql,new{tenantId,nodeId},cancellationToken:ct));
        return x is null?null:new(x.Id,x.ParentId,x.Code,x.Title,x.Description,x.ActivityType,x.DocumentFunction,x.NormativeSource,x.Keywords,x.DisplayOrder,x.ReviewStatus,x.IsActive);
    }

    public async Task<Guid> CreateNodeAsync(ClassificationNodeCreateCommand x,CancellationToken ct)
    { Validate(x.Code,x.Title,x.ParentId,null); var id=Guid.NewGuid(); await using var c=await _db.OpenAsync(ct);
      await EnsureParentAsync(c,x.TenantId,x.ParentId,null,ct);
      const string sql="""insert into ged.classification_node(id,tenant_id,parent_id,code,title,description,activity_type,document_function,normative_source,keywords,display_order,review_status,is_active) values(@id,@TenantId,@ParentId,trim(@Code),trim(@Title),@Description,@ActivityType,@DocumentFunction,@NormativeSource,@Keywords,@DisplayOrder,@ReviewStatus,@IsActive)""";
      await c.ExecuteAsync(new CommandDefinition(sql,new{id,x.TenantId,x.ParentId,x.Code,x.Title,x.Description,x.ActivityType,x.DocumentFunction,x.NormativeSource,x.Keywords,x.DisplayOrder,x.ReviewStatus,x.IsActive},cancellationToken:ct)); return id; }

    public async Task UpdateNodeAsync(ClassificationNodeUpdateCommand x,CancellationToken ct)
    { Validate(x.Code,x.Title,x.ParentId,x.Id); await using var c=await _db.OpenAsync(ct); await EnsureParentAsync(c,x.TenantId,x.ParentId,x.Id,ct);
      const string cycle="""with recursive descendants as (select id from ged.classification_node where tenant_id=@TenantId and parent_id=@Id and reg_status='A' union all select n.id from ged.classification_node n join descendants d on n.parent_id=d.id where n.tenant_id=@TenantId and n.reg_status='A') select exists(select 1 from descendants where id=@ParentId)""";
      if(x.ParentId.HasValue && await c.ExecuteScalarAsync<bool>(new CommandDefinition(cycle,new{x.TenantId,x.Id,x.ParentId},cancellationToken:ct))) throw new ArgumentException("A classe pai criaria um ciclo na árvore.");
      const string sql="""update ged.classification_node set parent_id=@ParentId,code=trim(@Code),title=trim(@Title),description=@Description,activity_type=@ActivityType,document_function=@DocumentFunction,normative_source=@NormativeSource,keywords=@Keywords,display_order=@DisplayOrder,review_status=@ReviewStatus,is_active=@IsActive,updated_at=now() where tenant_id=@TenantId and id=@Id and reg_status='A'""";
      if(await c.ExecuteAsync(new CommandDefinition(sql,x,cancellationToken:ct))==0) throw new KeyNotFoundException("Classe não encontrada."); }

    public async Task<IReadOnlyList<RetentionRuleListItem>> ListAsync(Guid tenantId,RetentionRuleFilter filter,CancellationToken ct)
    { const string sql="""select r.id,r.classification_node_id as ClassificationNodeId,n.code,n.title,r.current_phase_years as CurrentPhaseYears,r.intermediate_phase_years as IntermediatePhaseYears,r.final_destination as FinalDestination,r.trigger_event as TriggerEvent,r.legal_basis as LegalBasis,r.review_status as ReviewStatus from ged.retention_rule_v2 r join ged.classification_node n on n.id=r.classification_node_id and n.tenant_id=r.tenant_id where r.tenant_id=@tenantId and r.reg_status='A' and (nullif(@Search,'') is null or n.code ilike '%'||@Search||'%' or n.title ilike '%'||@Search||'%') and (nullif(@Status,'') is null or r.review_status=@Status) order by n.code""";
      await using var c=await _db.OpenAsync(ct); var rows=await c.QueryAsync<RuleDbRow>(new CommandDefinition(sql,new{tenantId,filter.Search,filter.Status},cancellationToken:ct)); return rows.Select(MapList).ToList(); }
    public async Task<RetentionRuleDetails?> GetByClassificationAsync(Guid tenantId,Guid classificationNodeId,CancellationToken ct)
    { const string sql="""select r.*,n.code,n.title from ged.retention_rule_v2 r join ged.classification_node n on n.id=r.classification_node_id and n.tenant_id=r.tenant_id where r.tenant_id=@tenantId and r.classification_node_id=@classificationNodeId and r.reg_status='A'"""; await using var c=await _db.OpenAsync(ct); var x=await c.QuerySingleOrDefaultAsync<RuleDbRow>(new CommandDefinition(sql,new{tenantId,classificationNodeId},cancellationToken:ct)); return x is null?null:Map(x); }
    public async Task SaveAsync(RetentionRuleSaveCommand x,CancellationToken ct)
    { if(x.CurrentPhaseYears<0||x.IntermediatePhaseYears<0) throw new ArgumentException("Prazos não podem ser negativos."); if(x.EffectiveTo<x.EffectiveFrom) throw new ArgumentException("Vigência final inválida."); if(x.FinalDestination=="AGUARDANDO_EVENTO"&&string.IsNullOrWhiteSpace(x.TriggerEvent)) throw new ArgumentException("Evento condicionante é obrigatório."); await using var c=await _db.OpenAsync(ct); await EnsureParentAsync(c,x.TenantId,x.ClassificationNodeId,null,ct);
      const string sql="""insert into ged.retention_rule_v2(id,tenant_id,classification_node_id,current_phase_years,intermediate_phase_years,final_destination,trigger_event,trigger_description,legal_basis,observation,review_status,effective_from,effective_to) values(coalesce(@Id,gen_random_uuid()),@TenantId,@ClassificationNodeId,@CurrentPhaseYears,@IntermediatePhaseYears,@FinalDestination,@TriggerEvent,@TriggerDescription,@LegalBasis,@Observation,@ReviewStatus,@EffectiveFrom,@EffectiveTo) on conflict (tenant_id,classification_node_id) where reg_status='A' do update set current_phase_years=excluded.current_phase_years,intermediate_phase_years=excluded.intermediate_phase_years,final_destination=excluded.final_destination,trigger_event=excluded.trigger_event,trigger_description=excluded.trigger_description,legal_basis=excluded.legal_basis,observation=excluded.observation,review_status=excluded.review_status,effective_from=excluded.effective_from,effective_to=excluded.effective_to,updated_at=now()"""; await c.ExecuteAsync(new CommandDefinition(sql,x,cancellationToken:ct)); }

    public async Task<IReadOnlyList<ClassificationVersionItem>> ListVersionsAsync(Guid tenantId,CancellationToken ct)
    { const string sql="""select id,version_number as VersionNumber,title,status,published_at as PublishedAt,published_by as PublishedBy,notes,coalesce(jsonb_array_length(snapshot_json->'classes'),0) as ClassCount,coalesce(jsonb_array_length(snapshot_json->'rules'),0) as RuleCount from ged.classification_plan_version_v2 where tenant_id=@tenantId and reg_status='A' order by version_number desc"""; await using var c=await _db.OpenAsync(ct); var rows=await c.QueryAsync<VersionDbRow>(new CommandDefinition(sql,new{tenantId},cancellationToken:ct)); return rows.Select(x=>new ClassificationVersionItem(x.Id,x.VersionNumber,x.Title,x.Status,x.PublishedAt,x.PublishedBy,x.Notes,x.ClassCount,x.RuleCount)).ToList(); }
    public async Task<Guid> PublishAsync(Guid tenantId,Guid userId,string notes,CancellationToken ct)
    { await using var c=await _db.OpenAsync(ct); const string sql="""with snap as (select jsonb_build_object('classes',coalesce((select jsonb_agg(to_jsonb(n) order by n.code) from ged.classification_node n where n.tenant_id=@tenantId and n.reg_status='A'),'[]'::jsonb),'rules',coalesce((select jsonb_agg(to_jsonb(r)) from ged.retention_rule_v2 r where r.tenant_id=@tenantId and r.reg_status='A'),'[]'::jsonb)) data), next as(select coalesce(max(version_number),0)+1 n from ged.classification_plan_version_v2 where tenant_id=@tenantId and reg_status='A') insert into ged.classification_plan_version_v2(tenant_id,version_number,title,status,published_at,published_by,notes,snapshot_json) select @tenantId,next.n,'PCD/TTD v'||next.n,'PUBLISHED',now(),@userId,@notes,snap.data from snap,next returning id"""; return await c.ExecuteScalarAsync<Guid>(new CommandDefinition(sql,new{tenantId,userId,notes},cancellationToken:ct)); }
    public async Task<ClassificationVersionCompareResult> CompareAsync(Guid tenantId,Guid fromVersionId,Guid toVersionId,CancellationToken ct)
    { await using var c=await _db.OpenAsync(ct); const string sql="""select id,snapshot_json::text as SnapshotJson from ged.classification_plan_version_v2 where tenant_id=@tenantId and id=any(@ids) and reg_status='A'"""; var rows=(await c.QueryAsync<SnapshotDbRow>(new CommandDefinition(sql,new{tenantId,ids=new[]{fromVersionId,toVersionId}},cancellationToken:ct))).ToDictionary(x=>x.Id); if(!rows.ContainsKey(fromVersionId)||!rows.ContainsKey(toVersionId)) throw new KeyNotFoundException("Versão não encontrada."); var a=ReadSnapshot(rows[fromVersionId].SnapshotJson); var b=ReadSnapshot(rows[toVersionId].SnapshotJson); var diffs=Diff(a,b); return new(fromVersionId,toVersionId,diffs); }

    private static Dictionary<string,string> ReadSnapshot(string json){using var d=JsonDocument.Parse(json); var r=new Dictionary<string,string>(); foreach(var group in new[]{"classes","rules"}) foreach(var e in d.RootElement.GetProperty(group).EnumerateArray()){var key=group+":"+(e.TryGetProperty("code",out var code)?code.GetString():e.GetProperty("classification_node_id").ToString()); r[key]=e.GetRawText();} return r;}
    private static IReadOnlyList<ClassificationVersionDifference> Diff(Dictionary<string,string>a,Dictionary<string,string>b){var r=new List<ClassificationVersionDifference>(); foreach(var k in a.Keys.Union(b.Keys).Order()){var entity=k.StartsWith("classes")?"CLASSE":"REGRA";var code=k[(k.IndexOf(':')+1)..];if(!a.ContainsKey(k))r.Add(new("ADICIONADO",entity,code,"Incluído na versão destino"));else if(!b.ContainsKey(k))r.Add(new("REMOVIDO",entity,code,"Ausente na versão destino"));else if(a[k]!=b[k])r.Add(new("ALTERADO",entity,code,"Conteúdo ou temporalidade alterado"));}return r;}
    private static RetentionRuleListItem MapList(RuleDbRow x)=>new(x.Id,x.ClassificationNodeId,x.Code,x.Title,x.CurrentPhaseYears,x.IntermediatePhaseYears,x.FinalDestination,x.TriggerEvent,x.LegalBasis,x.ReviewStatus);
    private static RetentionRuleDetails Map(RuleDbRow x)=>new(x.Id,x.ClassificationNodeId,x.Code,x.Title,x.CurrentPhaseYears,x.IntermediatePhaseYears,x.FinalDestination,x.TriggerEvent,x.TriggerDescription,x.LegalBasis,x.Observation,x.ReviewStatus,x.EffectiveFrom.HasValue?DateOnly.FromDateTime(x.EffectiveFrom.Value):null,x.EffectiveTo.HasValue?DateOnly.FromDateTime(x.EffectiveTo.Value):null);
    private static void Validate(string code,string title,Guid? parent,Guid? id){if(string.IsNullOrWhiteSpace(code))throw new ArgumentException("Código obrigatório.");if(string.IsNullOrWhiteSpace(title))throw new ArgumentException("Título obrigatório.");if(parent==id&&id.HasValue)throw new ArgumentException("Classe não pode ser pai dela mesma.");}
    private static async Task EnsureParentAsync(System.Data.Common.DbConnection c,Guid tenantId,Guid? parentId,Guid? ignored,CancellationToken ct){if(!parentId.HasValue)return;const string q="select exists(select 1 from ged.classification_node where tenant_id=@tenantId and id=@parentId and reg_status='A')";if(!await c.ExecuteScalarAsync<bool>(new CommandDefinition(q,new{tenantId,parentId},cancellationToken:ct)))throw new ArgumentException("Classe pai precisa existir.");}
    private sealed class DashboardDbRow{public int Classes{get;set;}public int ClassesWithoutRule{get;set;}public int RulesInReview{get;set;}public int PublishedVersion{get;set;}public int PendingChanges{get;set;}public int PermanentDestinations{get;set;}}
    private sealed class NodeDbRow{public Guid Id{get;set;}public Guid? ParentId{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public string? Description{get;set;}public string? ActivityType{get;set;}public string? DocumentFunction{get;set;}public string? NormativeSource{get;set;}public string? Keywords{get;set;}public int DisplayOrder{get;set;}public string ReviewStatus{get;set;}="";public bool IsActive{get;set;}public bool HasRetentionRule{get;set;}}
    private sealed class RuleDbRow{public Guid Id{get;set;}public Guid ClassificationNodeId{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public int? CurrentPhaseYears{get;set;}public int? IntermediatePhaseYears{get;set;}public string FinalDestination{get;set;}="";public string? TriggerEvent{get;set;}public string? TriggerDescription{get;set;}public string? LegalBasis{get;set;}public string? Observation{get;set;}public string ReviewStatus{get;set;}="";public DateTime? EffectiveFrom{get;set;}public DateTime? EffectiveTo{get;set;}}
    private sealed class VersionDbRow{public Guid Id{get;set;}public int VersionNumber{get;set;}public string Title{get;set;}="";public string Status{get;set;}="";public DateTimeOffset? PublishedAt{get;set;}public Guid? PublishedBy{get;set;}public string? Notes{get;set;}public int ClassCount{get;set;}public int RuleCount{get;set;}}
    private sealed class SnapshotDbRow{public Guid Id{get;set;}public string SnapshotJson{get;set;}="{}";}
}
