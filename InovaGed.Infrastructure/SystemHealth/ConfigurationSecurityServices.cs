using System.Text.RegularExpressions;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed class SecretMasker : ISecretMasker
{
    private static readonly Regex SecretRegex = new(
        @"(?i)(password|pwd|token|secret|key)\s*=\s*([^;\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(50));

    public string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var limited = value.Length > 4096 ? value[..4096] : value;

        try
        {
            var masked = SecretRegex.Replace(limited, match => $"{match.Groups[1].Value}=***");
            return masked.Length > 180 ? masked[..180] + "..." : masked;
        }
        catch (RegexMatchTimeoutException)
        {
            return "***";
        }
    }
}

public sealed class StartupConfigurationValidator : IStartupConfigurationValidator
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ISecretMasker _masker;
    private readonly IDatabaseConfigurationValidator _databaseConfigurationValidator;

    public StartupConfigurationValidator(
        IConfiguration configuration,
        IHostEnvironment environment,
        ISecretMasker masker,
        IDatabaseConfigurationValidator? databaseConfigurationValidator = null)
    {
        _configuration = configuration;
        _environment = environment;
        _masker = masker;
        _databaseConfigurationValidator = databaseConfigurationValidator ?? new PostgresConfigurationValidator();
    }

    public IReadOnlyList<StartupConfigurationCheck> Validate()
    {
        var env = _environment.EnvironmentName;
        var prod = _environment.IsProduction();
        var checks = new List<StartupConfigurationCheck>();
        var cs = _configuration.GetConnectionString("DefaultConnection");
        var databaseReport = _databaseConfigurationValidator.Validate(cs, prod);
        checks.AddRange(databaseReport.Checks);

        if (databaseReport.IsValid)
        {
            checks.Add(new StartupConfigurationCheck(
                "ConnectionStrings:DefaultConnection",
                "ok",
                StartupConfigurationSeverity.Info,
                _masker.Mask(cs),
                "Configure via variável ConnectionStrings__DefaultConnection, User Secrets, IIS ou Docker Secret.",
                "configuration",
                env) { Module = "Banco" });
        }

        AddPath(checks, "Storage:Local:RootPath", _configuration["Storage:Local:RootPath"], prod, env, "Use volume/pasta dedicada fora de TEMP e com ACL do AppPool.");
        AddPath(checks, "Preview:LibreOfficePath", _configuration["Preview:LibreOfficePath"], false, env, "Opcional: configure se preview Office for usado.");
        AddPath(checks, "Ocr:OcrMyPdfPath", _configuration["Ocr:OcrMyPdfPath"], false, env, "Opcional: configure OCRmyPDF globalmente ou PATH.");
        AddPath(checks, "Ocr:PythonPath", _configuration["Ocr:PythonPath"], false, env, "Opcional: configure Python globalmente ou PATH.");

        var tz = _configuration["Localization:DefaultTimeZone"] ?? _configuration["App:LocalTimeZoneId"];
        checks.Add(new("Timezone padrão", string.IsNullOrWhiteSpace(tz) ? "fallback" : "ok", StartupConfigurationSeverity.Info, _masker.Mask(tz ?? "America/Belem"), "Persistir UTC e apresentar pelo timezone do tenant; fallback America/Belem.", "Localization:DefaultTimeZone", env));

        ProdBlock(checks, prod && _configuration.GetValue<bool>("SystemSeed:Enabled"), "Seed habilitado", "Desabilite SystemSeed em Production.", "SystemSeed:Enabled", env);
        ProdBlock(checks, prod && _configuration.GetValue<bool>("Auth:AllowInternalSelfSignedCertificates"), "Certificado interno autoassinado permitido", "Use false em Production.", "Auth:AllowInternalSelfSignedCertificates", env);
        ProdBlock(checks, prod && _configuration.GetValue<bool>("SchemaRepair:Enabled") && _configuration.GetValue<bool>("SchemaRepair:AllowApplyInProduction"), "Schema repair automático", "Não permitir reparo automático em Production.", "SchemaRepair", env);
        ProdBlock(checks, prod && string.Equals(_configuration["AllowedHosts"], "*", StringComparison.Ordinal), "AllowedHosts irrestrito", "Configure hosts reais em Production.", "AllowedHosts", env);
        ProdBlock(checks, prod && _configuration.GetValue<bool>("DetailedErrors"), "DetailedErrors habilitado", "Desabilite páginas detalhadas em Production.", "DetailedErrors", env);
        ProdBlock(checks, prod && Guid.TryParse(_configuration["OcrAutoSchedule:SystemUserId"], out var u) && u == Guid.Empty, "Usuário de sistema vazio", "Configure usuário técnico explícito por tenant.", "OcrAutoSchedule:SystemUserId", env);
        return checks;
    }

    private static void Add(List<StartupConfigurationCheck> checks, string item, bool ok, bool criticalIfMissing, string masked, string rec, string source, string env) =>
        checks.Add(new(item, ok ? "ok" : "ausente", ok ? StartupConfigurationSeverity.Info : criticalIfMissing ? StartupConfigurationSeverity.Critical : StartupConfigurationSeverity.Warning, masked, rec, source, env) { Module = ModuleFor(item) });
    private static StartupConfigurationCheck Critical(string item, string status, string value, string rec, string source, string env) => new(item, status, StartupConfigurationSeverity.Critical, value, rec, source, env) { Module = ModuleFor(source) };
    private static void ProdBlock(List<StartupConfigurationCheck> checks, bool condition, string item, string rec, string source, string env) { if (condition) checks.Add(Critical(item, "bloqueado-em-producao", "true", rec, source, env)); }

    private static string ModuleFor(string key)
    {
        if (key.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Database", StringComparison.OrdinalIgnoreCase)) return "Banco";
        if (key.StartsWith("Storage", StringComparison.OrdinalIgnoreCase) || key.StartsWith("DocumentUpload", StringComparison.OrdinalIgnoreCase)) return "Storage";
        if (key.StartsWith("Ocr", StringComparison.OrdinalIgnoreCase)) return "OCR";
        if (key.StartsWith("Preview", StringComparison.OrdinalIgnoreCase)) return "Preview";
        if (key.StartsWith("Pacs", StringComparison.OrdinalIgnoreCase)) return "PACS";
        if (key.StartsWith("Workers", StringComparison.OrdinalIgnoreCase)) return "Workers";
        if (key.StartsWith("Auth", StringComparison.OrdinalIgnoreCase) || key.StartsWith("AllowedHosts", StringComparison.OrdinalIgnoreCase) || key.StartsWith("DetailedErrors", StringComparison.OrdinalIgnoreCase)) return "Segurança";
        if (key.StartsWith("SystemSeed", StringComparison.OrdinalIgnoreCase)) return "Seed";
        if (key.StartsWith("Audit", StringComparison.OrdinalIgnoreCase)) return "Auditoria";
        return "Startup";
    }

    private void AddPath(List<StartupConfigurationCheck> checks, string key, string? value, bool criticalIfMissing, string env, string rec)
    {
        var unsafePersonal = !string.IsNullOrWhiteSpace(value) && (value.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase) || value.Contains("Administrator", StringComparison.OrdinalIgnoreCase));
        checks.Add(new(key, string.IsNullOrWhiteSpace(value) ? "não configurado" : unsafePersonal ? "caminho-pessoal" : "ok", unsafePersonal ? StartupConfigurationSeverity.Warning : string.IsNullOrWhiteSpace(value) && criticalIfMissing ? StartupConfigurationSeverity.Critical : StartupConfigurationSeverity.Info, _masker.Mask(value), rec, key, env) { Module = ModuleFor(key) });
    }
}

public sealed class ExecutableResolver : IExecutableResolver
{
    public ExecutableResolution Resolve(string toolName, string? configuredPath, string environmentVariable, IReadOnlyList<string> knownPaths)
    {
        foreach (var (path, source) in new[] { (configuredPath, "configuração"), (Environment.GetEnvironmentVariable(environmentVariable), environmentVariable) })
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return new(toolName, true, path, source, "Disponível.");
        foreach (var path in knownPaths) if (File.Exists(path)) return new(toolName, true, path, "diretório conhecido", "Disponível.");
        var pathEnv = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Select(p => Path.Combine(p, toolName));
        var found = pathEnv.FirstOrDefault(File.Exists);
        return found is null ? new(toolName, false, null, "PATH", "Executável indisponível.") : new(toolName, true, found, "PATH", "Disponível.");
    }
}
