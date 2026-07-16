using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed record DatabaseConfigurationReport(
    IReadOnlyList<StartupConfigurationCheck> Checks,
    bool IsValid,
    bool HasPassword,
    bool UsesWeakPassword,
    string Database,
    string Username,
    string? Error);

public interface IDatabaseConfigurationValidator
{
    DatabaseConfigurationReport Validate(string? connectionString, bool isProduction);
}

public sealed class PostgresConfigurationValidator : IDatabaseConfigurationValidator
{
    private static readonly HashSet<string> WeakDatabasePasswords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "123456",
            "postgres",
            "admin",
            "admin@123",
            "password",
            "senha",
            "senha123",
            "12345678"
        };

    public DatabaseConfigurationReport Validate(string? connectionString, bool isProduction)
    {
        var analysis = AnalyzeConnectionString(connectionString);
        var env = isProduction ? Environments.Production : Environments.Development;
        var checks = new List<StartupConfigurationCheck>();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            checks.Add(Critical(
                "ConnectionStrings:DefaultConnection",
                "ausente",
                string.Empty,
                "Configure via variável ConnectionStrings__DefaultConnection, User Secrets, IIS ou Docker Secret.",
                "ConnectionStrings:DefaultConnection",
                env));
        }

        if (!string.IsNullOrWhiteSpace(connectionString) && !analysis.IsValid)
        {
            checks.Add(Critical(
                "Connection string PostgreSQL",
                "inválida",
                "***",
                "Corrija a estrutura de ConnectionStrings:DefaultConnection.",
                "ConnectionStrings:DefaultConnection",
                env));
        }

        if (analysis.IsValid && !analysis.HasPassword)
        {
            checks.Add(new StartupConfigurationCheck(
                "Senha PostgreSQL",
                "não definida na connection string",
                isProduction ? StartupConfigurationSeverity.Critical : StartupConfigurationSeverity.Warning,
                "***",
                "Configure a credencial por variável de ambiente, secret do IIS, Docker Secret ou cofre de segredos.",
                "ConnectionStrings:DefaultConnection",
                env) { Module = "Banco" });
        }

        if (analysis.UsesWeakPassword)
        {
            checks.Add(Critical(
                "Senha padrão PostgreSQL",
                "inseguro",
                "Password=***",
                "Troque imediatamente a senha e remova credenciais versionadas.",
                "ConnectionStrings:DefaultConnection",
                env));
        }

        if (analysis.IsValid && string.Equals(analysis.Database, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new StartupConfigurationCheck(
                "Banco de dados PostgreSQL",
                "banco administrativo padrão",
                StartupConfigurationSeverity.Warning,
                "postgres",
                "Crie e utilize um banco exclusivo chamado inovaged.",
                "ConnectionStrings:DefaultConnection",
                env) { Module = "Banco" });
        }

        if (analysis.IsValid && string.Equals(analysis.Username, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new StartupConfigurationCheck(
                "Usuário PostgreSQL",
                "superusuário",
                isProduction ? StartupConfigurationSeverity.Critical : StartupConfigurationSeverity.Warning,
                "postgres",
                "Crie um usuário exclusivo para a aplicação com permissões mínimas.",
                "ConnectionStrings:DefaultConnection",
                env) { Module = "Banco" });
        }

        return new DatabaseConfigurationReport(
            checks,
            analysis.IsValid,
            analysis.HasPassword,
            analysis.UsesWeakPassword,
            analysis.Database,
            analysis.Username,
            analysis.Error);
    }

    private static ConnectionStringSecurityAnalysis AnalyzeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new ConnectionStringSecurityAnalysis(false, false, false, string.Empty, string.Empty, "Connection string não configurada.");
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var password = builder.Password?.Trim();
            var usesWeakPassword = !string.IsNullOrWhiteSpace(password) && WeakDatabasePasswords.Contains(password);

            return new ConnectionStringSecurityAnalysis(
                true,
                !string.IsNullOrWhiteSpace(password),
                usesWeakPassword,
                builder.Database ?? string.Empty,
                builder.Username ?? string.Empty,
                null);
        }
        catch (ArgumentException)
        {
            return new ConnectionStringSecurityAnalysis(false, false, false, string.Empty, string.Empty, "Connection string PostgreSQL inválida.");
        }
    }

    private static StartupConfigurationCheck Critical(string item, string status, string value, string rec, string source, string env) =>
        new(item, status, StartupConfigurationSeverity.Critical, value, rec, source, env) { Module = "Banco" };

    private sealed record ConnectionStringSecurityAnalysis(
        bool IsValid,
        bool HasPassword,
        bool UsesWeakPassword,
        string Database,
        string Username,
        string? Error);
}
