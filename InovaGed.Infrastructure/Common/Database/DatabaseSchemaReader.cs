using Dapper;
using Npgsql;

namespace InovaGed.Infrastructure.Common.Database;

internal interface IDatabaseSchemaReader
{
    Task<bool> TableExistsAsync(NpgsqlConnection connection, string schema, string table, CancellationToken ct);
    Task<IReadOnlySet<string>> GetColumnsAsync(NpgsqlConnection connection, string schema, string table, CancellationToken ct);
}

internal sealed class DatabaseSchemaReader : IDatabaseSchemaReader
{
    public Task<bool> TableExistsAsync(NpgsqlConnection connection, string schema, string table, CancellationToken ct) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass(@qualifiedName) is not null", new { qualifiedName = $"{schema}.{table}" }, cancellationToken: ct));

    public async Task<IReadOnlySet<string>> GetColumnsAsync(NpgsqlConnection connection, string schema, string table, CancellationToken ct)
    {
        const string sql = "select column_name from information_schema.columns where table_schema = @schema and table_name = @table";
        return (await connection.QueryAsync<string>(new CommandDefinition(sql, new { schema, table }, cancellationToken: ct)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
