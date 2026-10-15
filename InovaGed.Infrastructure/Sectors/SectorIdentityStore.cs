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
left join ged.sector_patient c on c.id=p.canonical_patient_id and c.tenant_id=p.tenant_id
  and c.source_system=p.source_system and c.source_unit=p.source_unit and c.canonical_patient_id is null
where p.tenant_id=@tenantId and p.source_system=@system and p.source_unit=@unit
  and p.identifier_kind=@kind and p.external_code=@code
  and (p.canonical_patient_id is null or c.id is not null)
""", new { tenantId, system, unit, kind, code }, cancellationToken: ct));
        if (patient is null || patient.PatientId == Guid.Empty)
        {
            patient = await conn.QuerySingleOrDefaultAsync<PatientRow>(new CommandDefinition("""
select a.patient_id as PatientId, p.confirmed_at as ConfirmedAt
from ged.sector_patient_alias a
join ged.sector_patient p on p.id=a.patient_id and p.tenant_id=a.tenant_id
  and p.source_system=a.source_system and p.source_unit=a.source_unit and p.canonical_patient_id is null
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
        await using var tx = await conn.BeginTransactionAsync(ct);
        var target = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
select id from ged.sector_patient
where id=@canonical and tenant_id=@tenantId and source_system=@system and source_unit=@unit
  and canonical_patient_id is null and confirmed_at is not null
for update
""", new { tenantId, canonical = canonicalPatientId, system, unit }, tx, cancellationToken: ct));
        if (target is null)
            throw new InvalidOperationException("O paciente canônico não está confirmado nesta origem e unidade.");

        var source = await conn.QuerySingleOrDefaultAsync<IdentityLinkRow>(new CommandDefinition("""
select id as Id, canonical_patient_id as CanonicalPatientId
from ged.sector_patient
where tenant_id=@tenantId and source_system=@system and source_unit=@unit
  and identifier_kind=@kind and external_code=@code
for update
""", new { tenantId, system, unit, kind, code }, tx, cancellationToken: ct));
        if (source is not null && source.CanonicalPatientId.HasValue && source.CanonicalPatientId != canonicalPatientId)
            throw new InvalidOperationException("O identificador já pertence a outro paciente canônico.");
        if (source is not null && source.Id != canonicalPatientId)
        {
            var hasDependents = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(select 1 from ged.sector_patient where canonical_patient_id=@id)
""", new { id = source.Id }, tx, cancellationToken: ct));
            if (hasDependents)
                throw new InvalidOperationException("O vínculo criaria uma cadeia de identidades canônicas.");
        }
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_patient
set canonical_patient_id=@canonical, confirmed_at=coalesce(confirmed_at, now())
where tenant_id=@tenantId and source_system=@system and source_unit=@unit
  and identifier_kind=@kind and external_code=@code
  and id<>@canonical and canonical_patient_id is null
""", new { tenantId, canonical = canonicalPatientId, system, unit, kind, code }, tx, cancellationToken: ct));
        var linked = await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_patient_alias
 (id, tenant_id, patient_id, source_system, source_unit, identifier_kind, external_code, confirmed_at)
values
 (gen_random_uuid(), @tenantId, @canonical, @system, @unit, @kind, @code, now())
on conflict (tenant_id, source_system, source_unit, identifier_kind, external_code)
do update set confirmed_at=now()
where ged.sector_patient_alias.patient_id=excluded.patient_id
""", new { tenantId, canonical = canonicalPatientId, system, unit, kind, code }, tx, cancellationToken: ct));
        if (linked != 1)
            throw new InvalidOperationException("O identificador já está associado a outro paciente.");
        await tx.CommitAsync(ct);
    }

    private sealed class IdentityLinkRow
    {
        public Guid Id { get; set; }
        public Guid? CanonicalPatientId { get; set; }
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
