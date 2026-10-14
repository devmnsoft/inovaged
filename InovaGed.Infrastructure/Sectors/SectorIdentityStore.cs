using Dapper;
using Npgsql;

namespace InovaGed.Infrastructure.Sectors;

public static class SectorIdentityStore
{
    public sealed class Located
    {
        public Guid PatientId { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public Guid? EncounterId { get; set; }
        public DateTime? EncounterConfirmedAt { get; set; }
    }

    public static async Task<Located?> FindAsync(NpgsqlConnection conn, Guid tenantId, string system, string unit, string kind, string code, string encounterCode, CancellationToken ct)
    {
        var patient = await conn.QuerySingleOrDefaultAsync<PatientRow>(new CommandDefinition("""
select coalesce(p.canonical_patient_id, p.id) as PatientId,
       coalesce(c.confirmed_at, p.confirmed_at) as ConfirmedAt
from ged.sector_patient p
left join ged.sector_patient c on c.id=p.canonical_patient_id
where p.tenant_id=@tenantId and p.source_system=@system and p.source_unit=@unit
  and p.identifier_kind=@kind and p.external_code=@code
""", new { tenantId, system, unit, kind, code }, cancellationToken: ct));
        if (patient is null || patient.PatientId == Guid.Empty)
        {
            patient = await conn.QuerySingleOrDefaultAsync<PatientRow>(new CommandDefinition("""
select a.patient_id as PatientId, p.confirmed_at as ConfirmedAt
from ged.sector_patient_alias a
join ged.sector_patient p on p.id=a.patient_id and p.tenant_id=a.tenant_id
where a.tenant_id=@tenantId and a.source_system=@system and a.source_unit=@unit
  and a.identifier_kind=@kind and a.external_code=@code
""", new { tenantId, system, unit, kind, code }, cancellationToken: ct));
        }
        if (patient is null || patient.PatientId == Guid.Empty) return null;

        var encounter = await conn.QuerySingleOrDefaultAsync<EncounterRow>(new CommandDefinition("""
select id as EncounterId, confirmed_at as ConfirmedAt
from ged.sector_encounter
where tenant_id=@tenantId and patient_id=@patientId and external_code=@encounterCode
""", new { tenantId, patientId = patient.PatientId, encounterCode }, cancellationToken: ct));
        return new Located
        {
            PatientId = patient.PatientId,
            ConfirmedAt = patient.ConfirmedAt,
            EncounterId = encounter is null || encounter.EncounterId == Guid.Empty ? null : encounter.EncounterId,
            EncounterConfirmedAt = encounter?.ConfirmedAt
        };
    }

    public static async Task LinkAsync(NpgsqlConnection conn, Guid tenantId, Guid canonicalPatientId, string system, string unit, string kind, string code, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_patient
set canonical_patient_id=@canonical, confirmed_at=coalesce(confirmed_at, now())
where tenant_id=@tenantId and source_system=@system and source_unit=@unit
  and identifier_kind=@kind and external_code=@code
  and id<>@canonical and canonical_patient_id is null
""", new { tenantId, canonical = canonicalPatientId, system, unit, kind, code }, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_patient_alias
 (id, tenant_id, patient_id, source_system, source_unit, identifier_kind, external_code, confirmed_at)
values
 (gen_random_uuid(), @tenantId, @canonical, @system, @unit, @kind, @code, now())
on conflict (tenant_id, source_system, source_unit, identifier_kind, external_code)
do update set confirmed_at=now()
where ged.sector_patient_alias.patient_id=excluded.patient_id
""", new { tenantId, canonical = canonicalPatientId, system, unit, kind, code }, cancellationToken: ct));
    }

    private sealed class PatientRow
    {
        public Guid PatientId { get; set; }
        public DateTime? ConfirmedAt { get; set; }
    }

    private sealed class EncounterRow
    {
        public Guid EncounterId { get; set; }
        public DateTime? ConfirmedAt { get; set; }
    }
}
