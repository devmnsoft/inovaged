using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Sectors;
using Npgsql;

namespace InovaGed.Infrastructure.Sectors;

public static class SectorOpeningExchange
{
    public static async Task<SectorConsumedOpening?> ConsumeAsync(NpgsqlConnection conn, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 64) return null;
        ct.ThrowIfCancellationRequested();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CodeRow>(new CommandDefinition("""
select c.id as Id, c.tenant_id as TenantId, c.patient_id as PatientId, c.encounter_id as EncounterId,
       c.catalog_code as CatalogCode, c.document_id as DocumentId, c.version_id as VersionId,
       c.mode as Mode, c.scope_kind as ScopeKind, c.allow_encounter_switch as AllowEncounterSwitch,
       c.origin as Origin, c.integration_id as IntegrationId, c.correlation_id as CorrelationId, c.expires_at as ExpiresAt,
       c.granted_catalog as GrantedCatalog
from ged.sector_open_code c
join ged.sector_integration i on i.id=c.integration_id and i.tenant_id=c.tenant_id and i.active
join ged.sector_patient p on p.id=c.patient_id and p.tenant_id=c.tenant_id
  and p.source_system=i.source_system and p.source_unit=i.source_unit
join ged.sector_encounter e on e.id=c.encounter_id and e.tenant_id=c.tenant_id and e.patient_id=c.patient_id
where c.code_hash=@hash and c.consumed_at is null and c.expires_at>now()
for update of c
""", new { hash = Hash(code) }, tx, cancellationToken: ct));
        if (row is null) return null;
        var cookie = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var protection = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var sessionId = Guid.NewGuid();
        var publicId = Guid.NewGuid();
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_open_session
 (id, tenant_id, integration_id, cookie_hash, public_id, patient_id, encounter_id, catalog_code, document_id, version_id,
  mode, scope_kind, allow_encounter_switch, origin, correlation_id, expires_at, protection_hash, granted_catalog)
values
 (@sessionId, @tenantId, @integrationId, @cookieHash, @publicId, @patientId, @encounterId, @catalog, @documentId, @versionId,
  @mode, @scopeKind, @allowSwitch, @origin, @correlation, @expires, @protectionHash, @grantedCatalog)
""", new
        {
            sessionId, tenantId = row.TenantId, integrationId = row.IntegrationId, cookieHash = Hash(cookie), publicId,
            patientId = row.PatientId, encounterId = row.EncounterId, catalog = row.CatalogCode,
            documentId = row.DocumentId, versionId = row.VersionId, mode = row.Mode, scopeKind = row.ScopeKind,
            allowSwitch = row.AllowEncounterSwitch && row.ScopeKind == "atendimento",
            origin = row.Origin, correlation = row.CorrelationId, expires = row.ExpiresAt, protectionHash = Hash(protection),
            grantedCatalog = row.GrantedCatalog
        }, tx, cancellationToken: ct));
        var linked = await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_open_code
set consumed_at=now(), session_id=@sessionId
where id=@id and consumed_at is null
""", new { sessionId, id = row.Id }, tx, cancellationToken: ct));
        if (linked != 1)
            throw new InvalidOperationException("A abertura já estava consumida.");
        await tx.CommitAsync(ct);
        return new SectorConsumedOpening(cookie, publicId, row.TenantId, row.PatientId, row.EncounterId, row.CatalogCode, row.DocumentId, row.VersionId, row.Mode, row.ScopeKind, row.Origin, row.ExpiresAt, row.CorrelationId, protection);
    }

    public static async Task<SectorSessionAccess?> ReadAsync(NpgsqlConnection conn, string? cookieValue, Guid publicId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cookieValue) || cookieValue.Length != 64 || publicId == Guid.Empty) return null;
        var row = await conn.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition("""
select s.public_id as PublicId, s.tenant_id as TenantId, s.patient_id as PatientId, s.encounter_id as EncounterId,
       s.catalog_code as CatalogCode, s.document_id as DocumentId, s.version_id as VersionId, s.mode as Mode,
       s.scope_kind as ScopeKind,
       (s.allow_encounter_switch and i.allow_encounter_switch and s.scope_kind='atendimento') as AllowEncounterSwitch,
       s.origin as Origin, s.correlation_id as CorrelationId, s.expires_at as ExpiresAt,
       case
         when s.granted_catalog is null then i.catalog_codes
         when cardinality(i.catalog_codes)=0 then s.granted_catalog
         else coalesce((select array_agg(x) from unnest(s.granted_catalog) as x where x = any(i.catalog_codes)), '{}')
       end as AllowedCatalogCodes
from ged.sector_open_session s
join ged.sector_integration i on i.id=s.integration_id and i.tenant_id=s.tenant_id and i.active
join ged.sector_patient p on p.id=s.patient_id and p.tenant_id=s.tenant_id
  and p.source_system=i.source_system and p.source_unit=i.source_unit
join ged.sector_encounter e on e.id=s.encounter_id and e.tenant_id=s.tenant_id and e.patient_id=s.patient_id
where s.cookie_hash=@hash and s.public_id=@publicId and s.revoked_at is null and s.expires_at>now()
  and s.origin = any(i.origins)
  and (
    (s.scope_kind='atendimento' and i.allow_encounter_list and s.document_id is null and s.version_id is null)
    or (s.scope_kind='tipo' and s.catalog_code is not null and s.document_id is null
        and (cardinality(i.catalog_codes)=0 or s.catalog_code = any(i.catalog_codes))
        and (s.granted_catalog is null or s.catalog_code = any(s.granted_catalog))
        and exists (
          select 1 from ged.sector_document_type t
          where t.tenant_id=s.tenant_id and t.catalog_code=s.catalog_code and t.active))
    or (s.scope_kind='documento' and s.document_id is not null and s.version_id is not null
        and exists (
          select 1 from ged.sector_document_link l
          join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id and coalesce(d.reg_status,'A')='A'
          where l.tenant_id=s.tenant_id and l.encounter_id=s.encounter_id and l.patient_id=s.patient_id
            and l.document_id=s.document_id and l.version_id=s.version_id and l.reg_status='A'
            and (s.catalog_code is null or l.catalog_code=s.catalog_code)
            and (cardinality(i.catalog_codes)=0 or l.catalog_code = any(i.catalog_codes))
            and (s.granted_catalog is null or l.catalog_code = any(s.granted_catalog))
        ))
  )
""", new { hash = Hash(cookieValue), publicId }, cancellationToken: ct));
        if (row is null || row.PublicId == Guid.Empty) return null;
        var expires = row.ExpiresAt.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(row.ExpiresAt, DateTimeKind.Utc))
            : new DateTimeOffset(row.ExpiresAt.ToUniversalTime());
        return new SectorSessionAccess(row.PublicId, row.TenantId, row.PatientId, row.EncounterId, row.CatalogCode, row.DocumentId, row.VersionId, row.Mode, row.ScopeKind, row.AllowEncounterSwitch, row.Origin, row.CorrelationId, expires, row.AllowedCatalogCodes ?? []);
    }

    public static async Task<bool> SwitchAsync(NpgsqlConnection conn, string? cookieValue, Guid publicId, string encounterCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cookieValue) || string.IsNullOrWhiteSpace(encounterCode)) return false;
        var updated = await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_open_session s
set encounter_id=e.id, document_id=null, version_id=null
from ged.sector_integration i, ged.sector_encounter e
where s.cookie_hash=@hash and s.public_id=@publicId and s.revoked_at is null and s.expires_at>now()
  and s.scope_kind='atendimento' and s.allow_encounter_switch and s.document_id is null and s.version_id is null
  and i.id=s.integration_id and i.tenant_id=s.tenant_id and i.active and i.allow_encounter_switch and i.allow_encounter_list
  and s.origin = any(i.origins)
  and e.tenant_id=s.tenant_id and e.patient_id=s.patient_id and e.external_code=@code
  and exists (
    select 1 from ged.sector_patient p
    where p.id=s.patient_id and p.tenant_id=s.tenant_id
      and p.source_system=i.source_system and p.source_unit=i.source_unit)
""", new { hash = Hash(cookieValue), publicId, code = encounterCode.Trim() }, cancellationToken: ct));
        return updated > 0;
    }

    public static async Task<bool> AuthorizeAsync(NpgsqlConnection conn, string? cookieValue, Guid publicId, string? protectionToken, CancellationToken ct)
    {
        if (await ReadAsync(conn, cookieValue, publicId, ct) is null) return false;
        if (string.IsNullOrWhiteSpace(protectionToken) || protectionToken.Length != 64) return false;
        var expected = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select protection_hash
from ged.sector_open_session
where cookie_hash=@hash and public_id=@publicId and revoked_at is null and expires_at>now()
""", new { hash = Hash(cookieValue!), publicId }, cancellationToken: ct));
        var actual = Hash(protectionToken);
        if (string.IsNullOrWhiteSpace(expected) || expected.Length != actual.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));
    }

    public static async Task DeactivateAsync(NpgsqlConnection conn, Guid tenantId, string clientId, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        var integrationId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
update ged.sector_integration
set active=false
where tenant_id=@tenantId and client_id=@clientId and active
returning id
""", new { tenantId, clientId }, tx, cancellationToken: ct));
        if (integrationId == Guid.Empty) 
        {
            await tx.CommitAsync(ct);
            return;
        }
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_open_session set revoked_at=now()
where tenant_id=@tenantId and integration_id=@integrationId and revoked_at is null
""", new { tenantId, integrationId }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_open_code set consumed_at=now()
where tenant_id=@tenantId and integration_id=@integrationId and consumed_at is null
""", new { tenantId, integrationId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class SessionRow
    {
        public Guid PublicId { get; set; }
        public Guid TenantId { get; set; }
        public Guid PatientId { get; set; }
        public Guid EncounterId { get; set; }
        public string? CatalogCode { get; set; }
        public Guid? DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public string Mode { get; set; } = "consulta";
        public string ScopeKind { get; set; } = "atendimento";
        public bool AllowEncounterSwitch { get; set; }
        public string Origin { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public string[] AllowedCatalogCodes { get; set; } = [];
    }

    private sealed class CodeRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PatientId { get; set; }
        public Guid EncounterId { get; set; }
        public string? CatalogCode { get; set; }
        public Guid? DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public string Mode { get; set; } = "consulta";
        public string ScopeKind { get; set; } = "atendimento";
        public bool AllowEncounterSwitch { get; set; }
        public string Origin { get; set; } = "";
        public Guid IntegrationId { get; set; }
        public string CorrelationId { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public string[]? GrantedCatalog { get; set; }
    }
}
