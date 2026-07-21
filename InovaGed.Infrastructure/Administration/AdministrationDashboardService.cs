using Dapper;
using InovaGed.Application.Administration;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace InovaGed.Infrastructure.Administration;

public sealed class AdministrationDashboardService : IAdministrationDashboardService
{
    private readonly IDbConnectionFactory _db; private readonly IConfiguration _cfg; private readonly ISchemaHealthService? _schema;
    public AdministrationDashboardService(IDbConnectionFactory db, IConfiguration cfg, ISchemaHealthService? schema = null) { _db = db; _cfg = cfg; _schema = schema; }
    public async Task<AdministrationOverview> GetOverviewAsync(Guid? tenantId, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        var metrics = new List<AdministrationMetric>
        {
            await Count(c,"active_users","Usuários ativos","users","reg_status in ('A','ATIVO')",tenantId,ct,"bi-people"),
            await Count(c,"blocked_users","Usuários bloqueados","users","coalesce(is_blocked,false)=true",tenantId,ct,"bi-person-lock"),
            await Count(c,"active_tenants","Tenants ativos","tenants","reg_status in ('A','ATIVO')",null,ct,"bi-building"),
            await Count(c,"roles","Roles cadastradas","roles","1=1",null,ct,"bi-person-badge"),
            await Count(c,"permissions","Permissões cadastradas","permissions","1=1",null,ct,"bi-key"),
            await Count(c,"access_fail_24h","Falhas de acesso 24h","permission_evaluation_log","real_result=false and evaluated_at >= now() - interval '24 hours'",tenantId,ct,"bi-shield-exclamation"),
            await Count(c,"workers_error","Workers com erro","worker_execution_status","status in ('ERROR','FAILED')",tenantId,ct,"bi-cpu"),
            await Count(c,"queue_failed","Filas com falha","ged_processing_jobs","status in ('FAILED','ERROR')",tenantId,ct,"bi-list-x")
        };
        metrics.Add(new("database","Estado do banco","Conectado",AdministrationHealthState.Saudavel,null,"Conectividade validada pela consulta administrativa.","bi-database-check"));
        metrics.Add(StorageMetric());
        var rec = new List<AdministrationActionRecommendation>();
        if (metrics.Any(m => m.State != AdministrationHealthState.Saudavel)) rec.Add(new("Revisar indicadores indisponíveis","Algumas tabelas opcionais ainda não existem ou usam nomes legados.","Execute a central de Migrações e Compatibilidade antes de ativar ENFORCED.","Atenção"));
        rec.Add(new("Permissões em compatibilidade","O modo padrão LEGACY preserva o comportamento atual.","Use AUDIT_ONLY por tenant para medir divergências sem bloquear usuários.","Informativo"));
        return new(metrics, rec);
    }
    public async Task<IReadOnlyList<TenantSecurityConfiguration>> GetSecurityConfigurationsAsync(Guid? tenantId, CancellationToken ct = default)
    { await using var c = await _db.OpenAsync(ct); if (!await Table(c,"tenant_security_configuration",ct)) return Array.Empty<TenantSecurityConfiguration>(); return (await c.QueryAsync<TenantSecurityConfiguration>(new CommandDefinition("select tenant_id TenantId, permission_mode PermissionMode, changed_at ChangedAt, changed_by ChangedBy, change_reason ChangeReason, reg_status RegStatus from ged.tenant_security_configuration where (@tenantId is null or tenant_id=@tenantId) order by changed_at desc", new{tenantId}, cancellationToken:ct))).ToList(); }
    public async Task<IReadOnlyList<PermissionCatalogItem>> GetPermissionCatalogAsync(string? search, CancellationToken ct = default) { await using var c=await _db.OpenAsync(ct); var table=await Table(c,"permissions",ct)?"permissions":null; if(table is null) return Array.Empty<PermissionCatalogItem>(); return (await c.QueryAsync<PermissionCatalogItem>(new CommandDefinition("select code Code, coalesce(description,code) Description, coalesce(module,'Geral') Module, '' Roles, 0 UsersAffected, coalesce(reg_status,'ATIVO') Status, 'Banco' Origin, null::timestamptz LastChangedAt from ged.permissions where (@search is null or code ilike '%'||@search||'%' or description ilike '%'||@search||'%') order by module, code limit 200", new{search}, cancellationToken:ct))).ToList(); }
    public async Task<IdentityMigrationSummary> GetIdentityMigrationSummaryAsync(Guid? tenantId, CancellationToken ct = default) { await using var c=await _db.OpenAsync(ct); var total=await SafeInt(c,"users","1=1",tenantId,ct); var migrated=await SafeInt(c,"user_identity_document","document_type='CPF'",tenantId,ct); return new(total,0,migrated,0,Math.Max(0,total-migrated),0,0,Math.Max(0,total-migrated)); }
    public Task<IReadOnlyList<AdministrationListItem>> GetUsersAsync(Guid? t,CancellationToken ct=default)=>List("users","coalesce(name,username,email,id::text)","coalesce(reg_status,'ATIVO')","coalesce(email,'Sem e-mail')",t,ct);
    public Task<IReadOnlyList<AdministrationListItem>> GetAuditEventsAsync(Guid? t,CancellationToken ct=default)=>List("audit_logs","coalesce(action,event_type,id::text)","coalesce(result,'registrado')","coalesce(entity_type,module,'Auditoria')",t,ct);
    public Task<IReadOnlyList<AdministrationListItem>> GetTenantsAsync(Guid? t,bool g,CancellationToken ct=default)=>List("tenants","coalesce(name,code,id::text)","coalesce(reg_status,'ATIVO')","coalesce(code,id::text)",g?null:t,ct);
    public Task<IReadOnlyList<AdministrationListItem>> GetWorkersAsync(Guid? t,CancellationToken ct=default)=>List("worker_execution_status","worker_name","status","coalesce(message,'Sem mensagem')",t,ct);
    public async Task<IReadOnlyList<AdministrationListItem>> GetHealthAsync(CancellationToken ct=default){ if(_schema is null) return Array.Empty<AdministrationListItem>(); var r=await _schema.CheckAsync(ct); return r.Checks.Take(100).Select(x=>new AdministrationListItem(x.ObjectName,x.Success ? "saudável" : "atenção",x.Message,x.Area)).ToList(); }
    public Task<IReadOnlyList<AdministrationListItem>> GetSafeConfigurationsAsync(CancellationToken ct=default) => Task.FromResult<IReadOnlyList<AdministrationListItem>>(_cfg.AsEnumerable().Where(x=>x.Value is not null).Take(80).Select(x=>new AdministrationListItem(x.Key, IsSensitive(x.Key)?"Mascarado":"Configurado", IsSensitive(x.Key)?"********":x.Value!)).ToList());
    public Task<IReadOnlyList<AdministrationListItem>> GetMigrationsAsync(CancellationToken ct=default)=>List("schema_migration_history","script_name","coalesce(success,true)::text","coalesce(notes,'')",null,ct);
    public async Task<IReadOnlyList<ComplianceControlItem>> GetComplianceAsync(Guid? tenantId,CancellationToken ct=default){ var s=await GetIdentityMigrationSummaryAsync(tenantId,ct); return new[]{new ComplianceControlItem("LGPD-CPF","CPF protegido",s.LegacyDependent==0?"atendido":"parcialmente atendido",$"{s.AlreadyMigrated} migrados; {s.LegacyDependent} pendentes.","Migrar identidades sem expor CPF completo."),new ComplianceControlItem("AUDIT-STRICT","Auditoria estrita","não verificado","StrictAudit não é alterado automaticamente.","Avaliar risco e ativar com justificativa.")}; }
    private async Task<AdministrationMetric> Count(NpgsqlConnection c,string code,string title,string table,string where,Guid? tenantId,CancellationToken ct,string icon){ if(!await Table(c,table,ct)) return new(code,title,"Não disponível",AdministrationHealthState.Desconhecido,$"Tabela ged.{table} ausente ou equivalente legado não identificado.","Verifique Migrações e Compatibilidade.",icon); var v=await SafeInt(c,table,where,tenantId,ct); return new(code,title,v.ToString(),AdministrationHealthState.Saudavel,null,null,icon); }
    private async Task<int> SafeInt(NpgsqlConnection c,string table,string where,Guid? tenantId,CancellationToken ct){ try{ var hasTenant=await Column(c,table,"tenant_id",ct); return await c.ExecuteScalarAsync<int>(new CommandDefinition($"select count(*) from ged.{table} where {where} {(tenantId.HasValue&&hasTenant?" and tenant_id=@tenantId":"")}",new{tenantId},cancellationToken:ct));}catch{return 0;} }
    private async Task<IReadOnlyList<AdministrationListItem>> List(string table,string name,string status,string detail,Guid? tenantId,CancellationToken ct){ await using var c=await _db.OpenAsync(ct); if(!await Table(c,table,ct)) return Array.Empty<AdministrationListItem>(); var hasTenant=await Column(c,table,"tenant_id",ct); return (await c.QueryAsync<AdministrationListItem>(new CommandDefinition($"select {name} Name, {status} Status, {detail} Detail, {(hasTenant?"tenant_id::text":"null")} Tenant, null::timestamptz LastActivity from ged.{table} where (@tenantId is null or {(hasTenant?"tenant_id=@tenantId":"true")}) limit 200",new{tenantId},cancellationToken:ct))).ToList(); }
    private static Task<bool> Table(NpgsqlConnection c,string t,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass(@n) is not null",new{n=$"ged.{t}"},cancellationToken:ct));
    private static Task<bool> Column(NpgsqlConnection c,string t,string col,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from information_schema.columns where table_schema='ged' and table_name=@t and column_name=@col)",new{t,col},cancellationToken:ct));
    private AdministrationMetric StorageMetric(){var p=_cfg["Storage:Local:RootPath"]; if(string.IsNullOrWhiteSpace(p)) return new("storage","Estado do storage","Não configurado",AdministrationHealthState.NaoConfigurado,"Storage:Local:RootPath ausente.","Configure por provedor seguro.","bi-hdd"); return new("storage","Estado do storage",Directory.Exists(p)?"Disponível":"Indisponível",Directory.Exists(p)?AdministrationHealthState.Saudavel:AdministrationHealthState.Indisponivel,null,"Validar volume e permissões.","bi-hdd");}
    private static bool IsSensitive(string k)=>k.Contains("password",StringComparison.OrdinalIgnoreCase)||k.Contains("secret",StringComparison.OrdinalIgnoreCase)||k.Contains("token",StringComparison.OrdinalIgnoreCase)||k.Contains("connection",StringComparison.OrdinalIgnoreCase)||k.Contains("key",StringComparison.OrdinalIgnoreCase);
}
