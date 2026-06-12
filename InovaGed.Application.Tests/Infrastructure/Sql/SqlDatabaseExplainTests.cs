using Xunit;
namespace InovaGed.Application.Tests.Infrastructure.Sql;

public sealed class SqlDatabaseExplainTests
{
    [Trait("Category", "Database")]
    [Fact(Skip = "Opcional: configurar connection string de desenvolvimento e rodar EXPLAIN sem executar alterações.")]
    public void ExplainGeneratedQueries_AgainstDevelopmentPostgreSql()
    {
        // Intencionalmente opcional. Esta categoria deve ser habilitada apenas em ambiente com banco de desenvolvimento.
    }
}
