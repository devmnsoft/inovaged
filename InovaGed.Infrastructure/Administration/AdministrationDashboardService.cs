using Dapper;
using InovaGed.Application.Administration;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;
using InovaGed.Infrastructure.Common.Dapper;
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
            await CountActiveUsersAsync(c,tenantId,ct),
            await CountBlockedUsersAsync(c,tenantId,ct),
            await CountActiveTenantsAsync(c,ct),
            await Count(c,"roles","Roles cadastradas","app_role","1=1",null,ct,"bi-person-badge"),
            await Count(c,"permissions","Permissões cadastradas","permission","1=1",null,ct,"bi-key"),
            await CountEvaluationFailures(c,tenantId,ct),
            await CountByStatus(c,"workers_error","Workers com erro","worker_execution_status",tenantId,ct,"bi-cpu"),
            await CountByStatus(c,"queue_failed","Filas com falha","ged_processing_jobs",tenantId,ct,"bi-list-x")
        };
        metrics.Add(new("database","Estado do banco","Conectado",AdministrationHealthState.Saudavel,null,"Conectividade validada pela consulta administrativa.","bi-database-check"));
        metrics.Add(StorageMetric());
        var rec = new List<AdministrationActionRecommendation>();
        if (metrics.Any(m => m.State != AdministrationHealthState.Saudavel)) rec.Add(new("Revisar indicadores indisponíveis","Algumas tabelas opcionais ainda não existem ou usam nomes legados.","Execute a central de Migrações e Compatibilidade antes de ativar ENFORCED.","Atenção"));
        rec.Add(new("Permissões em compatibilidade","O modo padrão LEGACY preserva o comportamento atual.","Use AUDIT_ONLY por tenant para medir divergências sem bloquear usuários.","Informativo"));
        return new(metrics, rec);
    }
    public async Task<IReadOnlyList<TenantSecurityConfiguration>> GetSecurityConfigurationsAsync(Guid? tenantId, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        if (!await Table(c,"tenant_security_configuration",ct)) return Array.Empty<TenantSecurityConfiguration>();
        var columns=(await c.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name='tenant_security_configuration'",cancellationToken:ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("tenant_id")) return Array.Empty<TenantSecurityConfiguration>();
        var sql = $"""
select
    tenant_id as "TenantId",
    {InovaGed.Infrastructure.Common.Database.SchemaAwareSqlBuilder.ColumnOrLiteral(columns,"permission_mode","coalesce(permission_mode::text, 'LEGACY')","'LEGACY'")} as "PermissionMode",
    {InovaGed.Infrastructure.Common.Database.SchemaAwareSqlBuilder.ColumnOrLiteral(columns,"changed_at","changed_at","null::timestamp")} as "ChangedAt",
    {InovaGed.Infrastructure.Common.Database.SchemaAwareSqlBuilder.ColumnOrLiteral(columns,"changed_by","changed_by::text","null::text")} as "ChangedBy",
    {InovaGed.Infrastructure.Common.Database.SchemaAwareSqlBuilder.ColumnOrLiteral(columns,"change_reason","change_reason::text","null::text")} as "ChangeReason",
    {InovaGed.Infrastructure.Common.Database.SchemaAwareSqlBuilder.ColumnOrLiteral(columns,"reg_status","coalesce(reg_status::text, 'A')","'A'")} as "RegStatus"
from ged.tenant_security_configuration
where (@tenantId is null or tenant_id=@tenantId)
order by {InovaGed.Infrastructure.Common.Database.SchemaAwareSqlBuilder.ColumnOrLiteral(columns,"changed_at","changed_at","tenant_id")} desc
""";
        var rows = await c.QueryAsync<TenantSecurityConfigurationRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken:ct));
        return rows.Select(x => new TenantSecurityConfiguration(
            TenantId: x.TenantId,
            PermissionMode: DapperValueConverters.ParseEnumOrDefault(x.PermissionMode, PermissionMode.LEGACY),
            ChangedAt: DapperValueConverters.ToDateTimeOffset(x.ChangedAt) ?? DateTimeOffset.MinValue,
            ChangedBy: x.ChangedBy,
            ChangeReason: x.ChangeReason,
            RegStatus: string.IsNullOrWhiteSpace(x.RegStatus) ? "A" : x.RegStatus!)).ToList();
    }
    public async Task<IReadOnlyList<PermissionCatalogItem>> GetPermissionCatalogAsync(string? search, CancellationToken ct = default)
    {
        await using var c=await _db.OpenAsync(ct);
        if(!await Table(c,"permission",ct)) return Array.Empty<PermissionCatalogItem>();
        var schema=await GetAdminTableSchemaAsync(c,"permission",ct);
        var code=BuildPermissionCodeExpression(schema);
        var description=BuildPermissionDescriptionExpression(schema);
        var module=BuildPermissionModuleExpression(schema);
        var status=BuildStatusExpression(schema);
        var searchPredicate=BuildPermissionSearchPredicate(schema);
        var rows=await c.QueryAsync<PermissionCatalogItemDbRow>(new CommandDefinition($"""
select {code} as "Code", {description} as "Description",
       {module} as "Module", '' as "Roles", 0 as "UsersAffected",
       {status} as "Status", 'Banco' as "Origin", null::timestamp as "LastChangedAt"
from ged.permission
where (@search is null or btrim(@search) = '' or {searchPredicate})
order by "Module", "Code" limit 200
""",new{search},cancellationToken:ct));
        return rows.Select(x=>new PermissionCatalogItem(
            DapperValueConverters.TextOrDefault(x.Code,"Sem código"),
            DapperValueConverters.TextOrDefault(x.Description,x.Code??"Sem descrição"),
            DapperValueConverters.TextOrDefault(x.Module,"Geral"),x.Roles??string.Empty,x.UsersAffected,
            DapperValueConverters.TextOrDefault(x.Status,"A"),DapperValueConverters.TextOrDefault(x.Origin,"Banco"),
            DapperValueConverters.ToDateTimeOffset(x.LastChangedAt))).ToList();
    }
    public async Task<IdentityMigrationSummary> GetIdentityMigrationSummaryAsync(Guid? tenantId, CancellationToken ct = default) { await using var c=await _db.OpenAsync(ct); var schema=await GetAdminTableSchemaAsync(c,"app_user",ct); var total=await SafeInt(c,"app_user",schema.HasDeletedAtUtc?"deleted_at_utc is null":schema.HasDeletedAt?"deleted_at is null":"1=1",tenantId,ct); var migrated=await SafeInt(c,"user_identity_document","document_type='CPF'",tenantId,ct); return new(total,0,migrated,0,Math.Max(0,total-migrated),0,0,Math.Max(0,total-migrated)); }
    public Task<IReadOnlyList<AdministrationListItem>> GetUsersAsync(Guid? t,CancellationToken ct=default)=>List("app_user",t,ct);
    public Task<IReadOnlyList<AdministrationListItem>> GetAuditEventsAsync(Guid? t,CancellationToken ct=default)=>List("audit_logs",t,ct);
    public Task<IReadOnlyList<AdministrationListItem>> GetTenantsAsync(Guid? t,bool g,CancellationToken ct=default)=>List("tenant",g?null:t,ct);
    public Task<IReadOnlyList<AdministrationListItem>> GetWorkersAsync(Guid? t,CancellationToken ct=default)=>List("worker_execution_status",t,ct);
    public async Task<IReadOnlyList<AdministrationListItem>> GetHealthAsync(CancellationToken ct=default)
    {
        if (_schema is null)
            return [new AdministrationListItem("Schema", "indisponível", "Diagnóstico de schema temporariamente indisponível. Tente novamente.")];

        var report = await _schema.CheckAsync(ct);
        return report.Checks.Take(100).Select(x => new AdministrationListItem(
            x.ObjectName, x.Success ? "saudável" : "atenção", x.Message, x.Area)).ToList();
    }
    public Task<IReadOnlyList<AdministrationListItem>> GetSafeConfigurationsAsync(CancellationToken ct=default) => Task.FromResult<IReadOnlyList<AdministrationListItem>>(_cfg.AsEnumerable().Where(x=>x.Value is not null).Take(80).Select(x=>new AdministrationListItem(x.Key, IsSensitive(x.Key)?"Mascarado":"Configurado", IsSensitive(x.Key)?"********":x.Value!)).ToList());
    public Task<IReadOnlyList<AdministrationListItem>> GetMigrationsAsync(CancellationToken ct=default)=>List("schema_migration_history",null,ct);
    public async Task<IReadOnlyList<ComplianceControlItem>> GetComplianceAsync(Guid? tenantId,CancellationToken ct=default){ var s=await GetIdentityMigrationSummaryAsync(tenantId,ct); return new[]{new ComplianceControlItem("LGPD-CPF","CPF protegido",s.LegacyDependent==0?"atendido":"parcialmente atendido",$"{s.AlreadyMigrated} migrados; {s.LegacyDependent} pendentes.","Migrar identidades sem expor CPF completo."),new ComplianceControlItem("AUDIT-STRICT","Auditoria estrita","não verificado","StrictAudit não é alterado automaticamente.","Avaliar risco e ativar com justificativa.")}; }
    private async Task<AdministrationMetric> Count(NpgsqlConnection c,string code,string title,string table,string where,Guid? tenantId,CancellationToken ct,string icon){ if(!await Table(c,table,ct)) return new(code,title,"Não disponível",AdministrationHealthState.Desconhecido,$"Tabela ged.{table} ausente ou equivalente legado não identificado.","Verifique Migrações e Compatibilidade.",icon); var v=await SafeInt(c,table,where,tenantId,ct); return new(code,title,v.ToString(),AdministrationHealthState.Saudavel,null,null,icon); }
    private async Task<AdministrationMetric> CountByStatus(NpgsqlConnection c,string code,string title,string table,Guid? tenantId,CancellationToken ct,string icon){if(!await Table(c,table,ct))return MissingMetric(code,title,table,icon);var s=await GetAdminTableSchemaAsync(c,table,ct);var where=s.HasStatus?"status::text in ('ERROR','FAILED')":s.HasRegStatus?"reg_status::text in ('ERROR','FAILED','E')":null;if(where is null)return new(code,title,"Não disponível",AdministrationHealthState.Desconhecido,$"Coluna de status ausente em ged.{table}.","Aplique a migration de compatibilidade.",icon);var v=await SafeInt(c,table,where,tenantId,ct);return new(code,title,v.ToString(),AdministrationHealthState.Saudavel,null,null,icon);}
    private async Task<AdministrationMetric> CountEvaluationFailures(NpgsqlConnection c,Guid? tenantId,CancellationToken ct){const string table="permission_evaluation_log";if(!await Table(c,table,ct))return MissingMetric("access_fail_24h","Falhas de acesso 24h",table,"bi-shield-exclamation");var columns=(await c.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name=@table",new{table},cancellationToken:ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);if(!columns.Contains("real_result"))return new("access_fail_24h","Falhas de acesso 24h","Não disponível",AdministrationHealthState.Desconhecido,"Coluna real_result ausente.","Aplique a migration de compatibilidade.","bi-shield-exclamation");var where=columns.Contains("evaluated_at")?"real_result=false and evaluated_at >= now() - interval '24 hours'":"real_result=false";var v=await SafeInt(c,table,where,tenantId,ct);return new("access_fail_24h","Falhas de acesso 24h",v.ToString(),AdministrationHealthState.Saudavel,null,null,"bi-shield-exclamation");}
    private static AdministrationMetric MissingMetric(string code,string title,string table,string icon)=>new(code,title,"Não disponível",AdministrationHealthState.Desconhecido,$"Tabela ged.{table} ausente ou equivalente legado não identificado.","Execute database/migrations/2026_08_21_administration_legacy_schema_compat.sql ou database/apply_all_required_migrations.sql.",icon);
    private async Task<AdministrationMetric> CountActiveUsersAsync(NpgsqlConnection c,Guid? tenantId,CancellationToken ct){ if(!await Table(c,"app_user",ct)) return MissingMetric("active_users","Usuários ativos","app_user","bi-people"); var s=await GetAdminTableSchemaAsync(c,"app_user",ct); var f=new List<string>(); if(s.HasIsActive) f.Add("is_active=true"); if(s.HasDeletedAtUtc) f.Add("deleted_at_utc is null"); else if(s.HasDeletedAt) f.Add("deleted_at is null"); if(s.HasRegStatus) f.Add("coalesce(reg_status,'A')='A'"); var v=await SafeInt(c,"app_user",f.Count==0?"1=1":string.Join(" and ",f),tenantId,ct); return new("active_users","Usuários ativos",v.ToString(),AdministrationHealthState.Saudavel,null,null,"bi-people"); }
    private async Task<AdministrationMetric> CountBlockedUsersAsync(NpgsqlConnection c,Guid? tenantId,CancellationToken ct){ if(!await Table(c,"app_user",ct)) return MissingMetric("blocked_users","Usuários bloqueados","app_user","bi-person-lock"); var s=await GetAdminTableSchemaAsync(c,"app_user",ct); if(!s.HasIsLocked) return new("blocked_users","Usuários bloqueados","Não disponível",AdministrationHealthState.Desconhecido,"Coluna is_locked ausente em ged.app_user.","Execute as migrations administrativas ou use schema compatível.","bi-person-lock"); var f=new List<string>{"coalesce(is_locked,false)=true"}; if(s.HasDeletedAtUtc) f.Add("deleted_at_utc is null"); else if(s.HasDeletedAt) f.Add("deleted_at is null"); var v=await SafeInt(c,"app_user",string.Join(" and ",f),tenantId,ct); return new("blocked_users","Usuários bloqueados",v.ToString(),AdministrationHealthState.Saudavel,null,null,"bi-person-lock"); }
    private async Task<AdministrationMetric> CountActiveTenantsAsync(NpgsqlConnection c,CancellationToken ct){ if(!await Table(c,"tenant",ct)) return MissingMetric("active_tenants","Tenants ativos","tenant","bi-building"); var s=await GetAdminTableSchemaAsync(c,"tenant",ct); var f=s.HasIsActive?"is_active=true":s.HasRegStatus?"coalesce(reg_status,'A')='A'":"1=1"; var v=await SafeInt(c,"tenant",f,null,ct); return new("active_tenants","Tenants ativos",v.ToString(),AdministrationHealthState.Saudavel,null,null,"bi-building"); }
    private async Task<int> SafeInt(NpgsqlConnection c,string table,string where,Guid? tenantId,CancellationToken ct){ if(!AllowedAdministrationTables.Contains(table)) throw new InvalidOperationException($"Tabela administrativa não permitida: {table}"); var hasTenant=await Column(c,table,"tenant_id",ct); return await c.ExecuteScalarAsync<int>(new CommandDefinition($"select count(*) from ged.{table} where {where} {(tenantId.HasValue&&hasTenant?" and tenant_id=@tenantId":"")}",new{tenantId},cancellationToken:ct)); }
    private async Task<IReadOnlyList<AdministrationListItem>> List(string table,Guid? tenantId,CancellationToken ct)
    {
        await using var c=await _db.OpenAsync(ct);
        if(!await Table(c,table,ct)) return Array.Empty<AdministrationListItem>();
        var s=await GetAdminTableSchemaAsync(c,table,ct);
        var filter=tenantId.HasValue&&s.HasTenantId?"and tenant_id=@tenantId":"";
        var sql=$"""
select
    {BuildNameExpression(s)} as "Name",
    {BuildStatusExpression(s)} as "Status",
    {BuildDetailExpression(s)} as "Detail",
    {(s.HasTenantId?"tenant_id::text":"null::text")} as "Tenant",
    {BuildLastActivityExpression(s)} as "LastActivity"
from ged.{table}
where 1=1
{filter}
limit 200
""";
        var rows = await c.QueryAsync<AdministrationListItemRow>(new CommandDefinition(sql,new{tenantId},cancellationToken:ct));
        return rows.Select(x => new AdministrationListItem(
            Name: string.IsNullOrWhiteSpace(x.Name) ? "Sem identificação" : x.Name!,
            Status: string.IsNullOrWhiteSpace(x.Status) ? "ATIVO" : x.Status!,
            Detail: string.IsNullOrWhiteSpace(x.Detail) ? "Sem detalhe" : x.Detail!,
            Tenant: x.Tenant,
            LastActivity: DapperValueConverters.ToDateTimeOffset(x.LastActivity))).ToList();
    }
    private static Task<bool> Table(NpgsqlConnection c,string t,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass(@n) is not null",new{n=$"ged.{t}"},cancellationToken:ct));
    private static Task<bool> Column(NpgsqlConnection c,string t,string col,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from information_schema.columns where table_schema='ged' and table_name=@t and column_name=@col)",new{t,col},cancellationToken:ct));
    private async Task<AdminTableSchema> GetAdminTableSchemaAsync(NpgsqlConnection c,string table,CancellationToken ct)
    {
        if(!AllowedAdministrationTables.Contains(table)) throw new InvalidOperationException($"Tabela administrativa não permitida: {table}");
        const string sql="""select column_name from information_schema.columns where table_schema='ged' and table_name=@table""";
        var columns=(await c.QueryAsync<string>(new CommandDefinition(sql,new{table},cancellationToken:ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(columns.Contains("tenant_id"),columns.Contains("reg_status"),columns.Contains("status"),columns.Contains("is_active"),columns.Contains("deleted_at_utc"),columns.Contains("deleted_at"),columns.Contains("is_locked"),columns.Contains("name"),columns.Contains("user_name"),columns.Contains("email"),columns.Contains("code"),columns.Contains("id"),columns.Contains("created_at"),columns.Contains("updated_at"),columns.Contains("description"),columns.Contains("module"),columns.Contains("title"),columns.Contains("area"),columns.Contains("category"));
    }
    private static string BuildNameExpression(AdminTableSchema s){var p=new List<string>();if(s.HasName)p.Add("nullif(name::text,'')");if(s.HasUserName)p.Add("nullif(user_name::text,'')");if(s.HasEmail)p.Add("nullif(email::text,'')");if(s.HasCode)p.Add("nullif(code::text,'')");if(s.HasId)p.Add("id::text");return p.Count==0?"'Sem identificação'":$"coalesce({string.Join(", ",p)}, 'Sem identificação')";}
    private static string BuildStatusExpression(AdminTableSchema s)=>s.HasRegStatus?"coalesce(reg_status::text,'A')":s.HasStatus?"coalesce(status::text,'ATIVO')":s.HasIsActive?"case when is_active then 'ATIVO' else 'INATIVO' end":s.HasDeletedAtUtc?"case when deleted_at_utc is null then 'ATIVO' else 'EXCLUÍDO' end":s.HasDeletedAt?"case when deleted_at is null then 'ATIVO' else 'EXCLUÍDO' end":"'ATIVO'";
    private static string BuildDetailExpression(AdminTableSchema s){var p=new List<string>();if(s.HasEmail)p.Add("nullif(email::text,'')");if(s.HasCode)p.Add("nullif(code::text,'')");if(s.HasUserName)p.Add("nullif(user_name::text,'')");if(s.HasId)p.Add("id::text");return p.Count==0?"'Sem detalhe'":$"coalesce({string.Join(", ",p)}, 'Sem detalhe')";}
    private static string BuildLastActivityExpression(AdminTableSchema s)=>s.HasUpdatedAt?"updated_at":s.HasCreatedAt?"created_at":"null::timestamp";
    private static string BuildPermissionCodeExpression(AdminTableSchema s)=>s.HasCode?"code::text":s.HasName?"name::text":s.HasId?"id::text":"'SEM_CODIGO'";
    private static string BuildPermissionDescriptionExpression(AdminTableSchema s){var p=new List<string>();if(s.HasDescription)p.Add("nullif(description::text, '')");if(s.HasTitle)p.Add("nullif(title::text, '')");if(s.HasName)p.Add("nullif(name::text, '')");if(s.HasCode)p.Add("nullif(code::text, '')");if(s.HasId)p.Add("id::text");return p.Count==0?"'Permissão sem descrição'":$"coalesce({string.Join(", ",p)}, 'Permissão sem descrição')";}
    private static string BuildPermissionModuleExpression(AdminTableSchema s){var p=new List<string>();if(s.HasModule)p.Add("nullif(module::text, '')");if(s.HasArea)p.Add("nullif(area::text, '')");if(s.HasCategory)p.Add("nullif(category::text, '')");return p.Count==0?"'Geral'":$"coalesce({string.Join(", ",p)}, 'Geral')";}
    private static string BuildPermissionSearchPredicate(AdminTableSchema s){var p=new List<string>();if(s.HasCode)p.Add("code::text ilike '%' || @search || '%'");if(s.HasDescription)p.Add("description::text ilike '%' || @search || '%'");if(s.HasTitle)p.Add("title::text ilike '%' || @search || '%'");if(s.HasName)p.Add("name::text ilike '%' || @search || '%'");if(s.HasModule)p.Add("module::text ilike '%' || @search || '%'");if(s.HasArea)p.Add("area::text ilike '%' || @search || '%'");if(s.HasCategory)p.Add("category::text ilike '%' || @search || '%'");return p.Count==0?"1 = 1":"("+string.Join(" or ",p)+")";}
    private sealed class AdministrationListItemRow
    {
        public string? Name { get; set; }
        public string? Status { get; set; }
        public string? Detail { get; set; }
        public string? Tenant { get; set; }
        public DateTime? LastActivity { get; set; }
    }
    private sealed class TenantSecurityConfigurationRow
    {
        public Guid TenantId { get; set; }
        public string? PermissionMode { get; set; }
        public DateTime? ChangedAt { get; set; }
        public string? ChangedBy { get; set; }
        public string? ChangeReason { get; set; }
        public string? RegStatus { get; set; }
    }
    private sealed class PermissionCatalogItemDbRow
    {
        public string? Code { get; set; }
        public string? Description { get; set; }
        public string? Module { get; set; }
        public string? Roles { get; set; }
        public int UsersAffected { get; set; }
        public string? Status { get; set; }
        public string? Origin { get; set; }
        public DateTime? LastChangedAt { get; set; }
    }
    private sealed record AdminTableSchema(bool HasTenantId,bool HasRegStatus,bool HasStatus,bool HasIsActive,bool HasDeletedAtUtc,bool HasDeletedAt,bool HasIsLocked,bool HasName,bool HasUserName,bool HasEmail,bool HasCode,bool HasId,bool HasCreatedAt,bool HasUpdatedAt,bool HasDescription,bool HasModule,bool HasTitle,bool HasArea,bool HasCategory);
    private static readonly HashSet<string> AllowedAdministrationTables=new(StringComparer.OrdinalIgnoreCase){"app_user","tenant","app_role","permission","audit_logs","permission_evaluation_log","worker_execution_status","ged_processing_jobs","schema_migration_history","user_identity_document","tenant_security_configuration"};
    private AdministrationMetric StorageMetric(){var p=_cfg["Storage:Local:RootPath"]; if(string.IsNullOrWhiteSpace(p)) return new("storage","Estado do storage","Não configurado",AdministrationHealthState.NaoConfigurado,"Storage:Local:RootPath ausente.","Configure por provedor seguro.","bi-hdd"); return new("storage","Estado do storage",Directory.Exists(p)?"Disponível":"Indisponível",Directory.Exists(p)?AdministrationHealthState.Saudavel:AdministrationHealthState.Indisponivel,null,"Validar volume e permissões.","bi-hdd");}
    private static bool IsSensitive(string k)=>k.Contains("password",StringComparison.OrdinalIgnoreCase)||k.Contains("secret",StringComparison.OrdinalIgnoreCase)||k.Contains("token",StringComparison.OrdinalIgnoreCase)||k.Contains("connection",StringComparison.OrdinalIgnoreCase)||k.Contains("key",StringComparison.OrdinalIgnoreCase);
}
