using Dapper;
using Npgsql;

namespace InovaGed.Environment.Doctor.Quality;

public sealed class IncidentCenterQualityCheck : IQualityCheck
{
    public string Name => "incident-center-check";
    public async Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext context,CancellationToken cancellationToken)
    {
        if(string.IsNullOrWhiteSpace(context.ConnectionString)) return [new(Name,context.NoDatabaseRequired?QualityStatus.Warning:QualityStatus.Fail,"Banco não configurado; incidentes críticos não puderam ser verificados.")];
        await using var connection=new NpgsqlConnection(context.ConnectionString);await connection.OpenAsync(cancellationToken);
        if(!await connection.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.system_incident') is not null",cancellationToken:cancellationToken))) return [new(Name,QualityStatus.Fail,"Tabela ged.system_incident ausente.","O Quality Gate não consegue proteger a liberação.","Aplicar migrations obrigatórias.","database/migrations/2026_08_24_observability_incident_center.sql")];
        var count=await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
select count(*) from ged.system_incident where status in ('OPEN','IN_REVIEW') and severity in ('CRITICAL','HIGH')
and incident_type in ('DEPENDENCY_INJECTION','DAPPER_MATERIALIZATION','DATABASE_SCHEMA_MISSING_TABLE','DATABASE_SCHEMA_MISSING_COLUMN','RAZOR_COMPILATION') and reg_status='A'
""",cancellationToken:cancellationToken));
        return count==0?[new(Name,QualityStatus.Pass,"Nenhum incidente bloqueante aberto.")]:[new(Name,QualityStatus.Fail,$"Há {count} incidente(s) crítico(s) ou alto(s) bloqueante(s) aberto(s).","A liberação pode reproduzir falhas conhecidas.","Revise e resolva os incidentes na Central de Incidentes.",Resource:"/SystemIncidents")];
    }
}
