using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Governance;

namespace InovaGed.Infrastructure.Governance;

public sealed class GovernanceService(IDbConnectionFactory db) : IGovernanceDashboardService, IGovernanceAuditService, IGovernanceAlertService, IGovernanceEvidenceService, IGovernanceReportService
{
    private static readonly IReadOnlyDictionary<string, (string Title, string AlertType)> Reports = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["documents-without-ocr"] = ("Documentos sem OCR", GovernanceAlertType.DocumentWithoutOcr), ["documents-without-classification"] = ("Documentos sem classificação", GovernanceAlertType.DocumentWithoutClassification),
        ["sensitive-documents"] = ("Documentos sensíveis", GovernanceAlertType.SensitiveDataDetected), ["retention-pending"] = ("Temporalidade pendente", GovernanceAlertType.RetentionPending),
        ["labels-printed"] = ("Etiquetas impressas", "LABEL_PRINT_REGISTERED"), ["label-reprints"] = ("Reimpressões por período", GovernanceAlertType.LabelReprintWithoutReason),
        ["boxes-without-location"] = ("Caixas sem localização", GovernanceAlertType.BoxWithoutLocation), ["overdue-loans"] = ("Empréstimos vencidos", GovernanceAlertType.OverdueLoan),
        ["overdue-tasks"] = ("Tarefas atrasadas", GovernanceAlertType.WorkflowOverdue), ["critical-incidents"] = ("Incidentes críticos", GovernanceAlertType.CriticalIncidentOpen)
    };

    public async Task<GovernanceDashboard> GetDashboardAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        if (!await Ready(c, "governance_risk_snapshot", ct)) return new(0,0,0,0,0,0,0,0,0,0,false);
        var row = await c.QuerySingleOrDefaultAsync<DashboardDbRow>(new CommandDefinition("select documents_without_ocr DocumentsWithoutOcr,documents_without_classification DocumentsWithoutClassification,documents_with_sensitive_data SensitiveDocuments,retention_pending RetentionPending,boxes_without_location BoxesWithoutLocation,overdue_loans OverdueLoans,overdue_workflow_tasks OverdueTasks,open_critical_incidents CriticalIncidents from ged.governance_risk_snapshot where tenant_id=@tenantId and reg_status='A' order by generated_at desc limit 1", new { tenantId }, cancellationToken: ct)) ?? new();
        row.OpenAlerts = await ScalarCount(c, "governance_alert", "status in ('OPEN','IN_REVIEW')", tenantId, ct);
        row.ExportsThisMonth = await ScalarCount(c, "governance_report_log", "exported_at>=date_trunc('month',now())", tenantId, ct);
        return new(row.DocumentsWithoutOcr,row.DocumentsWithoutClassification,row.SensitiveDocuments,row.RetentionPending,row.BoxesWithoutLocation,row.OverdueLoans,row.OverdueTasks,row.CriticalIncidents,row.OpenAlerts,row.ExportsThisMonth,true);
    }

    public async Task<IReadOnlyList<GovernanceAuditItem>> ListAsync(GovernanceAuditFilter f, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        if (!await Ready(c, "app_audit_log", ct)) return [];
        var rows = await c.QueryAsync<AuditDbRow>(new CommandDefinition("""
select created_at OccurredAt,coalesce(action,event_type,'EVENT') EventType,coalesce(entity_name,source,'Sistema') Module,coalesce(user_name,'Usuário protegido') UserName,coalesce(source,path,'InovaGED') Origin,coalesce(message,action,'Evento registrado') Description,ip_address Ip,correlation_id CorrelationId
from ged.app_audit_log where tenant_id=@TenantId and coalesce(reg_status,'A')='A' and (@From is null or created_at>=@From) and (@To is null or created_at<=@To) and (@EventType is null or action=@EventType) and (@Module is null or entity_name=@Module) and (@Ip is null or ip_address=@Ip) and (@CorrelationId is null or correlation_id=@CorrelationId) and (@Search is null or message ilike '%'||@Search||'%' or action ilike '%'||@Search||'%') order by created_at desc limit 500
""", f, cancellationToken: ct));
        return rows.Select(x => new GovernanceAuditItem(ToOffset(x.OccurredAt),x.EventType,x.Module,x.UserName,x.Origin,Mask(x.Description),x.Ip,x.CorrelationId,null)).ToArray();
    }

    public async Task<IReadOnlyList<GovernanceAlertItem>> ListAsync(GovernanceAlertFilter f, CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct); if(!await Ready(c,"governance_alert",ct)) return [];
        var rows=await c.QueryAsync<AlertDbRow>(new CommandDefinition("select id Id,alert_type AlertType,severity Severity,title Title,description Description,source_type SourceType,source_id SourceId,recommended_action RecommendedAction,status Status,created_at CreatedAt from ged.governance_alert where tenant_id=@TenantId and reg_status='A' and (@Status is null or status=@Status) and (@Severity is null or severity=@Severity) and (@Type is null or alert_type=@Type) and (@SourceType is null or source_type=@SourceType) and (@AssignedTo is null or assigned_to=@AssignedTo) and (@From is null or created_at>=@From) and (@To is null or created_at<=@To) order by case severity when 'CRITICAL' then 1 when 'HIGH' then 2 when 'MEDIUM' then 3 else 4 end,created_at desc limit 500",f,cancellationToken:ct));
        return rows.Select(x=>new GovernanceAlertItem(x.Id,x.AlertType,x.Severity,x.Title,Mask(x.Description),x.SourceType,x.SourceId,x.RecommendedAction,x.Status,ToOffset(x.CreatedAt))).ToArray();
    }

    public async Task<Guid> CreateAsync(GovernanceAlertCreateCommand x,CancellationToken ct){ValidateAlert(x.AlertType,x.Severity);await using var c=await db.OpenAsync(ct);var id=Guid.NewGuid();await c.ExecuteAsync(new CommandDefinition("insert into ged.governance_alert(id,tenant_id,alert_type,severity,title,description,source_type,source_id,recommended_action) values(@id,@TenantId,@AlertType,@Severity,@Title,@Description,@SourceType,@SourceId,@RecommendedAction)",new{id,x.TenantId,x.AlertType,x.Severity,x.Title,Description=Mask(x.Description),x.SourceType,x.SourceId,x.RecommendedAction},cancellationToken:ct));return id;}
    public async Task ResolveAsync(Guid tenantId,Guid alertId,Guid userId,string notes,CancellationToken ct){if(string.IsNullOrWhiteSpace(notes))throw new ArgumentException("A observação de resolução é obrigatória.",nameof(notes));await using var c=await db.OpenAsync(ct);var n=await c.ExecuteAsync(new CommandDefinition("update ged.governance_alert set status='RESOLVED',resolved_by=@userId,resolved_at=now(),resolution_notes=@notes where tenant_id=@tenantId and id=@alertId and reg_status='A' and status in ('OPEN','IN_REVIEW')",new{tenantId,alertId,userId,notes=Mask(notes)},cancellationToken:ct));if(n==0)throw new InvalidOperationException("Alerta não encontrado ou já finalizado.");}
    public async Task<Guid> RegisterAsync(GovernanceEvidenceCreateCommand x,CancellationToken ct){await using var c=await db.OpenAsync(ct);var id=Guid.NewGuid();var code=$"EVD-{DateTime.UtcNow:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(5))}";var hash=string.IsNullOrWhiteSpace(x.PayloadJson)?null:Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.PayloadJson)));await c.ExecuteAsync(new CommandDefinition("insert into ged.governance_evidence(id,tenant_id,evidence_code,source_type,source_id,title,description,evidence_hash,payload_json,registered_by,registered_by_name) values(@id,@TenantId,@code,@SourceType,@SourceId,@Title,@Description,@hash,cast(@payload as jsonb),@UserId,@UserName)",new{id,x.TenantId,code,x.SourceType,x.SourceId,x.Title,Description=Mask(x.Description),hash,payload=string.IsNullOrWhiteSpace(x.PayloadJson)?null:JsonSerializer.Serialize(new{protectedPayload=true,originalHash=hash}),x.UserId,x.UserName},cancellationToken:ct));return id;}
    public async Task<IReadOnlyList<GovernanceEvidenceItem>> ListBySourceAsync(Guid tenantId,string? sourceType,Guid? sourceId,CancellationToken ct){await using var c=await db.OpenAsync(ct);if(!await Ready(c,"governance_evidence",ct))return[];var rows=await c.QueryAsync<EvidenceDbRow>(new CommandDefinition("select id Id,evidence_code EvidenceCode,source_type SourceType,source_id SourceId,title Title,description Description,evidence_hash EvidenceHash,registered_by_name RegisteredByName,registered_at RegisteredAt from ged.governance_evidence where tenant_id=@tenantId and reg_status='A' and (@sourceType is null or source_type=@sourceType) and (@sourceId is null or source_id=@sourceId) order by registered_at desc limit 500",new{tenantId,sourceType,sourceId},cancellationToken:ct));return rows.Select(x=>new GovernanceEvidenceItem(x.Id,x.EvidenceCode,x.SourceType,x.SourceId,x.Title,Mask(x.Description),x.EvidenceHash,x.RegisteredByName,ToOffset(x.RegisteredAt))).ToArray();}
    public async Task<GovernanceReportResult> GenerateAsync(GovernanceReportQuery q,CancellationToken ct){if(!Reports.TryGetValue(q.ReportType,out var report))throw new ArgumentException("Tipo de relatório inválido.");var alerts=await ListAsync(new(q.TenantId,Type:report.AlertType,From:q.From,To:q.To),ct);return new(q.ReportType,report.Title,alerts.Select(x=>new GovernanceReportRow(x.SourceId?.ToString()[..8]??"—",Mask(x.Title),x.Status,x.CreatedAt)).ToArray(),true);}
    public async Task<byte[]> ExportCsvAsync(GovernanceReportQuery q,CancellationToken ct){var r=await GenerateAsync(q,ct);var b=new StringBuilder("Referência;Descrição;Status;Data\r\n");foreach(var x in r.Rows)b.AppendLine($"{Csv(x.Reference)};{Csv(x.Description)};{Csv(x.Status)};{x.Date:O}");var bytes=Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(b.ToString())).ToArray();await using var c=await db.OpenAsync(ct);if(await Ready(c,"governance_report_log",ct))await c.ExecuteAsync(new CommandDefinition("insert into ged.governance_report_log(tenant_id,report_type,report_title,filters_json,exported_format,exported_by,exported_by_name,row_count) values(@TenantId,@ReportType,@title,cast(@filters as jsonb),'CSV',@UserId,@UserName,@count)",new{q.TenantId,q.ReportType,title=r.Title,filters=JsonSerializer.Serialize(new{q.From,q.To}),q.UserId,q.UserName,count=r.Rows.Count},cancellationToken:ct));return bytes;}

    private static async Task<bool> Ready(System.Data.Common.DbConnection c,string table,CancellationToken ct)=>await c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.'||@table) is not null",new{table},cancellationToken:ct));
    private static async Task<int> ScalarCount(System.Data.Common.DbConnection c,string table,string condition,Guid tenantId,CancellationToken ct)=>!await Ready(c,table,ct)?0:await c.ExecuteScalarAsync<int>(new CommandDefinition($"select count(*) from ged.{table} where tenant_id=@tenantId and reg_status='A' and {condition}",new{tenantId},cancellationToken:ct));
    private static DateTimeOffset ToOffset(DateTime value)=>new(DateTime.SpecifyKind(value,value.Kind==DateTimeKind.Unspecified?DateTimeKind.Utc:value.Kind));
    private static string? Mask(string? value){if(string.IsNullOrWhiteSpace(value))return value;value=Regex.Replace(value,@"\b(\d{3})\.?\d{3}\.?\d{3}[- ]?(\d{2})\b","$1.***.***-$2");return Regex.Replace(value,@"\b(\d{2})\.?\d{3}\.?\d{3}/?\d{4}[- ]?(\d{2})\b","$1.***.***/****-$2");}
    private static string Csv(string? value)=>$"\"{(value??string.Empty).Replace("\"","\"\"")}\"";
    private static void ValidateAlert(string type,string severity){if(!new[]{"CRITICAL","HIGH","MEDIUM","LOW","INFO"}.Contains(severity))throw new ArgumentException("Severidade inválida.");if(string.IsNullOrWhiteSpace(type)||type.Length>80)throw new ArgumentException("Tipo de alerta inválido.");}
    private sealed class DashboardDbRow{public int DocumentsWithoutOcr{get;set;}public int DocumentsWithoutClassification{get;set;}public int SensitiveDocuments{get;set;}public int RetentionPending{get;set;}public int BoxesWithoutLocation{get;set;}public int OverdueLoans{get;set;}public int OverdueTasks{get;set;}public int CriticalIncidents{get;set;}public int OpenAlerts{get;set;}public int ExportsThisMonth{get;set;}}
    private sealed class AuditDbRow{public DateTime OccurredAt{get;set;}public string EventType{get;set;}="";public string Module{get;set;}="";public string UserName{get;set;}="";public string Origin{get;set;}="";public string Description{get;set;}="";public string? Ip{get;set;}public string? CorrelationId{get;set;}}
    private sealed class AlertDbRow{public Guid Id{get;set;}public string AlertType{get;set;}="";public string Severity{get;set;}="";public string Title{get;set;}="";public string? Description{get;set;}public string? SourceType{get;set;}public Guid? SourceId{get;set;}public string? RecommendedAction{get;set;}public string Status{get;set;}="";public DateTime CreatedAt{get;set;}}
    private sealed class EvidenceDbRow{public Guid Id{get;set;}public string EvidenceCode{get;set;}="";public string SourceType{get;set;}="";public Guid? SourceId{get;set;}public string Title{get;set;}="";public string? Description{get;set;}public string? EvidenceHash{get;set;}public string? RegisteredByName{get;set;}public DateTime RegisteredAt{get;set;}}
}
