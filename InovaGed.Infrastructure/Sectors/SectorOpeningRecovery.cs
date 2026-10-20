using System.Text.Json;
using Dapper;
using InovaGed.Application.Sectors;
using Npgsql;

namespace InovaGed.Infrastructure.Sectors;

public sealed partial class SectorOpeningService
{
    public const int MaxRecoveries = 3;

    private static string ConfigurationFingerprint(IntegrationRow integration)=>Hash(JsonSerializer.Serialize(new
    {
        integration.Id,integration.TenantId,integration.ClientId,integration.SecretHash,integration.SourceSystem,integration.SourceUnit,
        integration.AllowEncounterList,integration.AllowEncounterSwitch,integration.MaxMinutes,integration.Active,
        Origins=integration.Origins.OrderBy(x=>x,StringComparer.Ordinal).ToArray(),
        Catalog=integration.CatalogCodes.OrderBy(x=>x,StringComparer.Ordinal).ToArray()
    }));

    private static string Fingerprint(SectorOpeningRequest request, IntegrationRow integration, string origin)
        => Hash(JsonSerializer.Serialize(new
        {
            integration.Id,
            Kind = request.IdentifierKind?.Trim().ToUpperInvariant(),
            Patient = string.Equals(request.IdentifierKind?.Trim(),"CPF",StringComparison.OrdinalIgnoreCase)
                ? SectorCpf.Normalize(request.Identifier) : request.Identifier?.Trim(),
            System = request.SourceSystem?.Trim() is { Length: > 0 } system ? system : integration.SourceSystem,
            Unit = request.SourceUnit?.Trim() ?? "", Encounter = request.EncounterCode?.Trim(),
            Name = request.DocumentName?.Trim(), Code = request.DocumentCode?.Trim(), request.DocumentId, request.VersionId,
            Mode = string.IsNullOrWhiteSpace(request.Mode) ? "consulta" : request.Mode.Trim().ToLowerInvariant(), Origin=origin
        }));

    private static async Task<SectorOpeningResponse?> ExistingRequestAsync(NpgsqlConnection conn, IntegrationRow integration,
        string key, string fingerprint, CancellationToken ct, NpgsqlTransaction? tx = null)
    {
        var previous = await conn.QuerySingleOrDefaultAsync<RequestIdentity>(new CommandDefinition("""
select request_fingerprint as Fingerprint,correlation_id as CorrelationId,revoked_at as RevokedAt from ged.sector_open_request
where integration_id=@id and idempotency_key=@key
""",new { id=integration.Id,key },tx,cancellationToken:ct));
        if (previous is null) return null;
        if (previous.RevokedAt.HasValue) return Refuse(SectorOpeningCodes.Denied,"A solicitação foi revogada. Inicie outra abertura autorizada com nova chave.",previous.CorrelationId);
        if (previous.Fingerprint != fingerprint)
            return Refuse(SectorOpeningCodes.Conflict,"A chave pertence a outro conteúdo ou a uma solicitação anterior ao contrato de recuperação.",previous.CorrelationId);
        return new(true,SectorOpeningCodes.Repeated,"Solicitação já registrada. Consulte o estado; para obter novos links use a recuperação autenticada.",null,null,null,null,previous.CorrelationId,0);
    }

    private async Task<IntegrationRow?> AuthenticateIntegrationAsync(NpgsqlConnection conn,string? clientId,string? secret,CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret)) return null;
        var integrations=(await conn.QueryAsync<IntegrationRow>(new CommandDefinition("""
select id as Id,tenant_id as TenantId,client_id as ClientId,secret_hash as SecretHash,source_system as SourceSystem,
 source_unit as SourceUnit,origins as Origins,catalog_codes as CatalogCodes,allow_encounter_list as AllowEncounterList,
 allow_encounter_switch as AllowEncounterSwitch,max_minutes as MaxMinutes,active as Active
from ged.sector_integration where client_id=@clientId and active
""",new { clientId=clientId.Trim() },cancellationToken:ct))).ToArray();
        return integrations.Length==1 && SecretMatches(integrations[0].SecretHash,secret) ? integrations[0] : null;
    }

    public async Task<SectorOpeningState> OpeningStateAsync(string? clientId,string? secret,string? key,CancellationToken ct)
    {
        await using var conn=await _db.OpenAsync(ct);
        var integration=await AuthenticateIntegrationAsync(conn,clientId,secret,ct);
        if (integration is null) return new(false,SectorOpeningCodes.Unauthorized,"nao_autorizada",null,null,0);
        if (!SectorEntryContract.IdempotencyKey(key,out var normalized) || normalized is null)
            return new(true,SectorOpeningCodes.Invalid,"invalida",null,null,0);
        var row=await conn.QuerySingleOrDefaultAsync<RequestState>(new CommandDefinition("""
select r.correlation_id as CorrelationId,r.recovery_count as Recoveries,c.expires_at as ExpiresAt,
 case when r.revoked_at is not null then 'revogada'
      when r.request_fingerprint is null then 'legada'
      when not i.active or not c.origin=any(i.origins) then 'revogada'
      when c.expires_at<=now() then 'expirada'
      when c.consumed_at is not null then 'utilizada' else 'disponivel' end as State
from ged.sector_open_request r
join ged.sector_open_code c on c.code_hash=r.frame_code_hash and c.integration_id=r.integration_id
join ged.sector_integration i on i.id=r.integration_id
where r.integration_id=@id and r.idempotency_key=@key
""",new { id=integration.Id,key=normalized },cancellationToken:ct));
        return row is null ? new(true,SectorOpeningCodes.Invalid,"ausente",null,null,0)
            : new(true,SectorOpeningCodes.Repeated,row.State,row.CorrelationId,row.ExpiresAt,row.Recoveries);
    }

    public async Task<SectorOpeningResponse> RecoverOpeningAsync(string? clientId,string? secret,string? key,CancellationToken ct)
    {
        var correlation=Guid.NewGuid().ToString("N");
        await using var conn=await _db.OpenAsync(ct);
        var integration=await AuthenticateIntegrationAsync(conn,clientId,secret,ct);
        if (integration is null) return Refuse(SectorOpeningCodes.Unauthorized,"A integração não foi autorizada.",correlation);
        if (!SectorEntryContract.IdempotencyKey(key,out var normalized) || normalized is null)
            return Refuse(SectorOpeningCodes.Invalid,"Informe a chave da solicitação.",correlation);
        await using var tx=await conn.BeginTransactionAsync(ct);
        // Same lock as issuance and registration: serializes concurrent recovery and configuration changes.
        var current=await conn.QuerySingleOrDefaultAsync<IntegrationRow>(new CommandDefinition("""
select id as Id,tenant_id as TenantId,client_id as ClientId,secret_hash as SecretHash,source_system as SourceSystem,
 source_unit as SourceUnit,origins as Origins,catalog_codes as CatalogCodes,allow_encounter_list as AllowEncounterList,
 allow_encounter_switch as AllowEncounterSwitch,max_minutes as MaxMinutes,active as Active
from ged.sector_integration where id=@id for update
""",new { id=integration.Id },tx,cancellationToken:ct));
        if (current is null || !current.Active || current.SecretHash!=integration.SecretHash)
            return Refuse(SectorOpeningCodes.Unauthorized,"A integração não foi autorizada.",correlation);
        var row=await conn.QuerySingleOrDefaultAsync<RecoveryGrant>(new CommandDefinition("""
select r.id as RequestId,r.correlation_id as CorrelationId,r.recovery_count as Recoveries,
 c.patient_id as PatientId,c.encounter_id as EncounterId,c.catalog_code as CatalogCode,c.document_id as DocumentId,
 c.version_id as VersionId,c.mode as Mode,c.scope_kind as ScopeKind,c.allow_encounter_switch as AllowSwitch,
 c.origin as Origin,c.expires_at as ExpiresAt,c.granted_catalog as GrantedCatalog
from ged.sector_open_request r join ged.sector_open_code c on c.integration_id=r.integration_id and c.code_hash=r.frame_code_hash
where r.integration_id=@id and r.idempotency_key=@key and r.request_fingerprint is not null
 and r.revoked_at is null
 and c.expires_at>now() and c.authorizing_user_id is null
for update of r,c
""",new { id=integration.Id,key=normalized },tx,cancellationToken:ct));
        if (row is null) return Refuse(SectorOpeningCodes.Invalid,"A solicitação não pode ser recuperada. Inicie outra abertura autorizada.",correlation);
        if (row.Recoveries>=MaxRecoveries) return Refuse(SectorOpeningCodes.RateLimited,"O limite de recuperação desta solicitação foi atingido.",row.CorrelationId);
        if (!OriginAllowed(current.Origins,row.Origin,out _)) return Refuse(SectorOpeningCodes.Denied,"A origem foi revogada.",row.CorrelationId);
        var valid=await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
 select 1 from ged.sector_patient p join ged.sector_encounter e on e.patient_id=p.id and e.tenant_id=p.tenant_id
 where p.tenant_id=@tenantId and p.id=@patientId and e.id=@encounterId
 and p.source_system=@system and p.source_unit=@unit
 and (@scope<>'atendimento' or @allowList)
 and exists(select 1 from ged.sector_document_type t where t.tenant_id=p.tenant_id and t.active
   and (@catalog is null or t.catalog_code=@catalog)
   and (cardinality(@catalogs)=0 or t.catalog_code=any(@catalogs))
   and (@granted::text[] is null or t.catalog_code=any(@granted)))
 and (@documentId::uuid is null or exists(
   select 1 from ged.sector_document_link l join ged.document d on d.id=l.document_id and d.tenant_id=l.tenant_id
   join ged.sector_document_type t on t.tenant_id=l.tenant_id and t.catalog_code=l.catalog_code and t.active
   where l.tenant_id=p.tenant_id and l.patient_id=p.id and l.encounter_id=e.id and l.reg_status='A'
    and l.document_id=@documentId and l.version_id=@versionId and coalesce(d.reg_status,'A')='A'
    and (@catalog is null or l.catalog_code=@catalog)
    and (cardinality(@catalogs)=0 or l.catalog_code=any(@catalogs))
    and (@granted::text[] is null or l.catalog_code=any(@granted))))
)
""",new { tenantId=current.TenantId,patientId=row.PatientId,encounterId=row.EncounterId,system=current.SourceSystem,unit=current.SourceUnit,
            scope=row.ScopeKind,allowList=current.AllowEncounterList,catalog=row.CatalogCode,catalogs=current.CatalogCodes,granted=row.GrantedCatalog,
            documentId=row.DocumentId,versionId=row.VersionId },tx,cancellationToken:ct));
        if (!valid) return Refuse(SectorOpeningCodes.Denied,"O contexto não está mais autorizado.",row.CorrelationId);
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_open_code set consumed_at=coalesce(consumed_at,now()) where integration_id=@id and correlation_id=@correlation;
update ged.sector_open_session set revoked_at=coalesce(revoked_at,now()) where integration_id=@id and correlation_id=@correlation;
""",new { id=current.Id,correlation=row.CorrelationId },tx,cancellationToken:ct));
        var page=NewSecret(); var frame=NewSecret();
        foreach(var code in new[] { page,frame })
            await InsertCodeAsync(conn,tx,current,code,row.PatientId,row.EncounterId,row.CatalogCode,row.DocumentId,row.VersionId,row.Mode,row.ScopeKind,
                row.AllowSwitch && current.AllowEncounterSwitch,row.Origin,row.CorrelationId,row.ExpiresAt,row.GrantedCatalog,ct,null);
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_open_request set recovery_count=recovery_count+1,page_code_hash=@page,frame_code_hash=@frame where id=@id
""",new { id=row.RequestId,page=Hash(page),frame=Hash(frame) },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return new(true,SectorOpeningCodes.Opened,"Concessão recuperada. Os acessos anteriores foram invalidados; o vencimento original foi mantido.",
            SectorOpeningLink.Path(page),SectorOpeningLink.Path(frame),row.ExpiresAt,row.ScopeKind,row.CorrelationId,0);
    }

    public async Task<SectorRecoveryInfo> RecoveryInfoAsync(Guid reference,bool entryTicket,CancellationToken ct)
    {
        await using var conn=await _db.OpenAsync(ct);
        var context=await RecoveryDraftAsync(conn,reference,entryTicket,ct);
        return new(context is not null,context?.Draft.Origin);
    }

    public async Task<SectorEntryIssue> CreateRecoveryEntryAsync(Guid reference,bool entryTicket,CancellationToken ct)
    {
        await using var conn=await _db.OpenAsync(ct);
        var context=await RecoveryDraftAsync(conn,reference,entryTicket,ct);
        return context is null ? new(false,null) : await CreateEntryCoreAsync(context.Draft with { Origin=null,Destination="pagina" },context.Configuration,ct);
    }

    public async Task<SectorEntryIssue> CreateCodeRecoveryEntryAsync(string code,CancellationToken ct)
    {
        if (code.Length!=64 || !code.All(char.IsAsciiHexDigit)) return new(false,null);
        await using var conn=await _db.OpenAsync(ct);
        var row=await conn.QuerySingleOrDefaultAsync<RecoveryDraft>(new CommandDefinition("""
select i.id as IntegrationId,i.source_system as SourceSystem,i.source_unit as SourceUnit,i.client_id as ClientId,p.identifier_kind as IdentifierKind,p.external_code as Identifier,e.external_code as EncounterCode,
 c.catalog_code as DocumentCode,c.document_id as DocumentId,c.version_id as VersionId,c.mode as Mode,c.origin as Origin
from ged.sector_open_code c join ged.sector_integration i on i.id=c.integration_id and i.tenant_id=c.tenant_id and i.active
join ged.sector_patient p on p.id=c.patient_id and p.tenant_id=c.tenant_id and p.source_system=i.source_system and p.source_unit=i.source_unit
join ged.sector_encounter e on e.id=c.encounter_id and e.tenant_id=c.tenant_id and e.patient_id=p.id
where c.code_hash=@hash and c.expires_at>now()-make_interval(hours=>@retention)
 and (c.origin='' or c.origin=any(i.origins))
 and not exists(select 1 from ged.sector_open_request r where r.integration_id=c.integration_id and r.correlation_id=c.correlation_id and r.revoked_at is not null)
""",new { hash=Hash(code),retention=_temporaryContexts.RetentionHours },cancellationToken:ct));
        if (row is null) return new(false,null);
        var configuration=await RecoveryConfigurationAsync(conn,row,ct);
        if (configuration is null) return new(false,null);
        // A consumed/expired code is only a context reference here, never an access credential.
        return await CreateEntryCoreAsync(new(row.ClientId,row.IdentifierKind,row.Identifier,row.EncounterCode,null,row.DocumentCode,row.DocumentId,row.VersionId,row.Mode,
            string.IsNullOrEmpty(row.Origin)?null:row.Origin,string.IsNullOrEmpty(row.Origin)?"pagina":"iframe"),configuration,ct);
    }

    private async Task<RecoveryDraftContext?> RecoveryDraftAsync(NpgsqlConnection conn,Guid reference,bool entryTicket,CancellationToken ct)
    {
        var sql=entryTicket ? """
select i.id as IntegrationId,i.source_system as SourceSystem,i.source_unit as SourceUnit,t.integration_fingerprint as IntegrationFingerprint,i.client_id as ClientId,t.identifier_kind as IdentifierKind,t.identifier_value as Identifier,t.encounter_code as EncounterCode,
 t.document_name as DocumentName,t.document_code as DocumentCode,t.document_id as DocumentId,t.version_id as VersionId,
 t.mode as Mode,t.origin as Origin
from ged.sector_entry_ticket t join ged.sector_integration i on i.id=t.integration_id and i.tenant_id=t.tenant_id and i.active
where t.id=@reference and t.expires_at>now()-make_interval(hours=>@retention)
 and t.revoked_at is null
 and (t.origin='' or t.origin=any(i.origins))
""" : """
select i.id as IntegrationId,i.source_system as SourceSystem,i.source_unit as SourceUnit,i.client_id as ClientId,p.identifier_kind as IdentifierKind,p.external_code as Identifier,e.external_code as EncounterCode,
 null::text as DocumentName,s.catalog_code as DocumentCode,s.document_id as DocumentId,s.version_id as VersionId,s.mode as Mode,s.origin as Origin
from ged.sector_open_session s join ged.sector_integration i on i.id=s.integration_id and i.tenant_id=s.tenant_id and i.active
join ged.sector_patient p on p.id=s.patient_id and p.tenant_id=s.tenant_id and p.source_system=i.source_system and p.source_unit=i.source_unit
join ged.sector_encounter e on e.id=s.encounter_id and e.tenant_id=s.tenant_id and e.patient_id=p.id
where s.public_id=@reference and s.revoked_at is null and s.expires_at>now()-make_interval(hours=>@retention)
 and (s.origin='' or s.origin=any(i.origins))
""";
        // Use a mutable row: Dapper constructor binding must not depend on optional record parameters.
        var row=await conn.QuerySingleOrDefaultAsync<RecoveryDraft>(new CommandDefinition(sql,new { reference,retention=_temporaryContexts.RetentionHours },cancellationToken:ct));
        if (row is null || (entryTicket && row.IntegrationFingerprint is null)) return null;
        var configuration=await RecoveryConfigurationAsync(conn,row,ct);
        if (configuration is null || (entryTicket && row.IntegrationFingerprint!=configuration)) return null;
        return new(new(row.ClientId,row.IdentifierKind,row.Identifier,row.EncounterCode,row.DocumentName,row.DocumentCode,row.DocumentId,row.VersionId,row.Mode,
            string.IsNullOrEmpty(row.Origin)?null:row.Origin,string.IsNullOrEmpty(row.Origin)?"pagina":"iframe"),configuration);
    }

    private static async Task<string?> RecoveryConfigurationAsync(NpgsqlConnection conn,RecoveryDraft row,CancellationToken ct)
    {
        var current=await conn.QuerySingleOrDefaultAsync<IntegrationRow>(new CommandDefinition("""
select i.id as Id,i.tenant_id as TenantId,i.client_id as ClientId,i.secret_hash as SecretHash,i.source_system as SourceSystem,
 i.source_unit as SourceUnit,i.origins as Origins,i.catalog_codes as CatalogCodes,i.allow_encounter_list as AllowEncounterList,
 i.allow_encounter_switch as AllowEncounterSwitch,i.max_minutes as MaxMinutes,i.active as Active
from ged.sector_integration i where i.id=@id and i.active
""",new { id=row.IntegrationId },cancellationToken:ct));
        return current is null || current.SourceSystem!=row.SourceSystem || current.SourceUnit!=row.SourceUnit
            || (row.Origin.Length>0 && !OriginAllowed(current.Origins,row.Origin,out _)) ? null : ConfigurationFingerprint(current);
    }

    private sealed class RequestIdentity { public string? Fingerprint { get; set; } public string CorrelationId { get; set; }=""; public DateTime? RevokedAt { get; set; } };
    private sealed class RequestState { public string CorrelationId { get; set; }=""; public string State { get; set; }=""; public int Recoveries { get; set; } public DateTimeOffset ExpiresAt { get; set; } };
    private sealed class RecoveryGrant
    {
        public Guid RequestId { get; set; } public string CorrelationId { get; set; }=""; public int Recoveries { get; set; }
        public Guid PatientId { get; set; } public Guid EncounterId { get; set; } public string? CatalogCode { get; set; }
        public Guid? DocumentId { get; set; } public Guid? VersionId { get; set; } public string Mode { get; set; }="";
        public string ScopeKind { get; set; }=""; public bool AllowSwitch { get; set; } public string Origin { get; set; }="";
        public DateTimeOffset ExpiresAt { get; set; } public string[]? GrantedCatalog { get; set; }
    }
    private sealed class RecoveryDraft
    {
        public Guid IntegrationId { get; set; } public string SourceSystem { get; set; }=""; public string SourceUnit { get; set; }="";
        public string? IntegrationFingerprint { get; set; }
        public string ClientId { get; set; }=""; public string IdentifierKind { get; set; }=""; public string Identifier { get; set; }="";
        public string EncounterCode { get; set; }=""; public string? DocumentName { get; set; } public string? DocumentCode { get; set; }
        public Guid? DocumentId { get; set; } public Guid? VersionId { get; set; } public string Mode { get; set; }=""; public string Origin { get; set; }="";
    }
    private sealed record RecoveryDraftContext(SectorEntryDraft Draft,string Configuration);
}
