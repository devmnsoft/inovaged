using Dapper;
using InovaGed.Application.Common.Codes;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Common.Codes;

public sealed class CodeGeneratorService : ICodeGeneratorService
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CodeGeneratorService> _logger;

    public CodeGeneratorService(IDbConnectionFactory db, IConfiguration configuration, ILogger<CodeGeneratorService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<string> GenerateNextCodeAsync(Guid tenantId, string entityName, string? prefix, CancellationToken ct)
    {
        var configuredPrefix = _configuration[$"CodeGeneration:Entities:{entityName}:Prefix"];
        var effectivePrefix = NormalizePrefix(prefix) ?? NormalizePrefix(configuredPrefix) ?? DerivePrefix(entityName);
        var padding = _configuration.GetValue<int?>( $"CodeGeneration:Entities:{entityName}:Padding")
            ?? _configuration.GetValue<int?>("CodeGeneration:DefaultPadding")
            ?? 4;

        return GenerateAsync(tenantId, entityName, effectivePrefix, padding, ct);
    }

    public Task<string> GenerateNextNumericCodeAsync(Guid tenantId, string entityName, int padding, CancellationToken ct)
        => GenerateAsync(tenantId, entityName, null, padding <= 0 ? 4 : padding, ct);

    private async Task<string> GenerateAsync(Guid tenantId, string entityName, string? prefix, int padding, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant inválido para geração de código.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(entityName)) throw new ArgumentException("Entidade obrigatória para geração de código.", nameof(entityName));

        entityName = entityName.Trim();
        prefix = NormalizePrefix(prefix) ?? "COD";
        padding = Math.Clamp(padding <= 0 ? 4 : padding, 2, 12);

        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            const string selectSql = """
select id as "Id", current_value as "CurrentValue", prefix as "Prefix", padding as "Padding"
from ged.code_sequence
where tenant_id = @TenantId
  and entity_name = @EntityName
  and coalesce(reg_status,'A') = 'A'
for update;
""";

            var args = new { TenantId = tenantId, EntityName = entityName, Prefix = prefix, Padding = padding };
            await conn.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(cast(@TenantId as text) || ':' || @EntityName, 0))", args, tx, cancellationToken: ct));
            var row = await conn.QueryFirstOrDefaultAsync<SequenceRow>(new CommandDefinition(selectSql, args, tx, cancellationToken: ct));
            long next;
            string? finalPrefix;
            int finalPadding;

            if (row is null)
            {
                next = 1;
                const string insertSql = """
insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding, created_at, updated_at, reg_status)
values (@TenantId, @EntityName, @Prefix, 1, @Padding, now(), now(), 'A')
returning current_value as "CurrentValue", prefix as "Prefix", padding as "Padding";
""";
                row = await conn.QuerySingleAsync<SequenceRow>(new CommandDefinition(insertSql, args, tx, cancellationToken: ct));
                finalPrefix = row.Prefix;
                finalPadding = row.Padding;
            }
            else
            {
                const string updateSql = """
update ged.code_sequence
   set current_value = current_value + 1,
       prefix = @Prefix,
       padding = @Padding,
       updated_at = now()
 where tenant_id = @TenantId
   and entity_name = @EntityName
   and coalesce(reg_status,'A') = 'A'
returning current_value as "CurrentValue", prefix as "Prefix", padding as "Padding";
""";
                row = await conn.QuerySingleAsync<SequenceRow>(new CommandDefinition(updateSql, args, tx, cancellationToken: ct));
                next = row.CurrentValue;
                finalPrefix = row.Prefix;
                finalPadding = row.Padding;
            }

            await tx.CommitAsync(ct);

            var number = next.ToString().PadLeft(finalPadding, '0');
            var code = $"{NormalizePrefix(finalPrefix) ?? "COD"}-{number}";
            _logger.LogInformation("Código gerado. Tenant={TenantId} Entity={EntityName} Code={Code}", tenantId, entityName, code);
            return code;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException("Tabela ged.code_sequence não existe. Execute as migrations do sistema.", ex);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static string? NormalizePrefix(string? value)
    {
        value = value?.Trim().Trim('-').ToUpperInvariant();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string DerivePrefix(string entityName)
    {
        var letters = new string(entityName.Where(char.IsLetter).Take(4).ToArray()).ToUpperInvariant();
        return string.IsNullOrWhiteSpace(letters) ? "COD" : letters;
    }

    private sealed class SequenceRow
    {
        public long CurrentValue { get; init; }
        public string? Prefix { get; init; }
        public int Padding { get; init; }
    }
}
