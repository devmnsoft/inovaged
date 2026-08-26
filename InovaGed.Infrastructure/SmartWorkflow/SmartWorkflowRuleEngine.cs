using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SmartWorkflow;
namespace InovaGed.Infrastructure.SmartWorkflow;
public sealed class SmartWorkflowRuleEngine(IDbConnectionFactory db,ISmartWorkflowTaskService tasks):ISmartWorkflowRuleEngine
{
 public async Task<IReadOnlyList<SmartWorkflowTaskCreateCommand>> DetectPendingTasksAsync(Guid tenantId,CancellationToken ct){await using var c=await db.OpenAsync(ct);var rows=await c.QueryAsync<PendingRow>(new CommandDefinition("""
select id SourceId,document_id DocumentId,'document_classification_suggestion' SourceType,'CLASSIFICATION_SUGGESTION_REVIEW' TaskType,'Revisar sugestão de classificação' Title,'Confirme ou rejeite a sugestão; nenhuma classificação será alterada automaticamente.' Action,case when confidence>=80 then 'HIGH' when confidence>=50 then 'MEDIUM' else 'LOW' end Priority from ged.document_classification_suggestion where tenant_id=@tenantId and status='PENDING' and reg_status='A'
union all select id,document_id,'document_retention_suggestion','RETENTION_SUGGESTION_REVIEW','Revisar sugestão de temporalidade','Confirme ou rejeite a sugestão sem alterar a temporalidade oficial.','MEDIUM' from ged.document_retention_suggestion where tenant_id=@tenantId and status='PENDING' and reg_status='A'
union all select id,document_id,'document_quality_issue','QUALITY_ISSUE_REVIEW',title,recommended_action,severity from ged.document_quality_issue where tenant_id=@tenantId and status='OPEN' and reg_status='A'
union all select id,document_id,'document_ai_analysis','SENSITIVE_DATA_REVIEW','Revisar indicador de dado sensível','Acesse o documento com autorização e revise o indicador; o conteúdo sensível não é copiado para a tarefa.','HIGH' from ged.document_ai_analysis where tenant_id=@tenantId and detected_sensitive_data is not null and detected_sensitive_data<> '[]'::jsonb and reg_status='A'
union all select id,null,'system_incident','INCIDENT_REVIEW',title,'Revisar o incidente; o workflow não o resolve automaticamente.',severity from ged.system_incident where (tenant_id=@tenantId or tenant_id is null) and status in ('OPEN','IN_REVIEW') and severity in ('CRITICAL','HIGH') and reg_status='A'
union all select id,target_id,'smart_assistant_action_suggestion','ASSISTANT_ACTION_REVIEW',title,'Revisar e decidir a ação sugerida; ela não será executada automaticamente.','MEDIUM' from ged.smart_assistant_action_suggestion where tenant_id=@tenantId and status='PENDING' and reg_status='A'
""",new{tenantId},cancellationToken:ct));return rows.Select(r=>new SmartWorkflowTaskCreateCommand(tenantId,r.SourceType,r.SourceId,r.TaskType,r.Title,null,r.Action,Normalize(r.Priority),r.DocumentId,null,r.SourceType=="system_incident"?r.SourceId:null,r.SourceId)).ToArray();}
 public async Task<int> GenerateTasksAsync(Guid tenantId,Guid? performedBy,CancellationToken ct){var detected=await DetectPendingTasksAsync(tenantId,ct);var before=(await tasks.ListAsync(new(tenantId),ct)).Select(x=>x.Id).ToHashSet();foreach(var x in detected)await tasks.CreateAsync(x with{OpenedBy=performedBy},ct);var after=await tasks.ListAsync(new(tenantId),ct);return after.Count(x=>!before.Contains(x.Id));}
 private static string Normalize(string? p)=>p is "CRITICAL" or "HIGH" or "LOW"?p:SmartWorkflowPriority.Medium;
 internal sealed class PendingRow{public Guid SourceId{get;set;}public Guid? DocumentId{get;set;}public string SourceType{get;set;}="";public string TaskType{get;set;}="";public string Title{get;set;}="";public string? Action{get;set;}public string? Priority{get;set;}}
}
