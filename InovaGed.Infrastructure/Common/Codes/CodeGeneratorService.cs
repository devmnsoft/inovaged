using Dapper;
using InovaGed.Application.Common.Codes;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant inválido.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(entityName)) throw new ArgumentException("Entidade inválida.", nameof(entityName));

        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string insertSql = """
insert into ged.code_sequence(tenant_id, entity_name, prefix, current_value, padding)
values (@TenantId, @EntityName, @Prefix, 0, @Padding)
on conflict (tenant_id, entity_name) do nothing;
""";

        const string updateSql = """
update ged.code_sequence
   set current_value = current_value + 1,
       prefix = coalesce(nullif(@Prefix, ''), prefix),
       padding = greatest(@Padding, 1),
       updated_at = now()
 where tenant_id = @TenantId
   and entity_name = @EntityName
returning current_value as "CurrentValue", prefix as "Prefix", padding as "Padding";
""";

        var args = new { TenantId = tenantId, EntityName = entityName.Trim(), Prefix = prefix, Padding = Math.Max(1, padding) };
        await conn.ExecuteAsync(new CommandDefinition(insertSql, args, tx, cancellationToken: ct));
        var row = await conn.QuerySingleAsync<SequenceRow>(new CommandDefinition(updateSql, args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        var number = row.CurrentValue.ToString().PadLeft(Math.Max(1, row.Padding), '0');
        var finalPrefix = NormalizePrefix(row.Prefix);
        var code = string.IsNullOrWhiteSpace(finalPrefix) ? number : $"{finalPrefix}-{number}";
        _logger.LogInformation("Código gerado. Tenant={TenantId} Entity={EntityName} Code={Code}", tenantId, entityName, code);
        return code;
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
