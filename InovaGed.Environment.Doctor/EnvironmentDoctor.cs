using System.Diagnostics;
using InovaGed.Application.EnvironmentDiagnostics;
using InovaGed.Application.Readiness;
using InovaGed.Infrastructure.Readiness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using BclEnvironment = global::System.Environment;

namespace InovaGed.Environment.Doctor;

public sealed class EnvironmentDoctor(IConfiguration configuration, Func<string, CancellationToken, Task<ModuleReadinessResult>>? readiness = null)
{
    private readonly Func<string, CancellationToken, Task<ModuleReadinessResult>> readiness = readiness ??
        new PostgresModuleReadinessService(configuration, NullLogger<PostgresModuleReadinessService>.Instance).GetAsync;

    public async Task<IReadOnlyList<EnvironmentCheckResult>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<EnvironmentCheckResult>();
        results.AddRange(CheckToolchain());
        results.Add(await CheckDatabaseAsync(cancellationToken));
        results.Add(CheckStorage());
        results.Add(CheckExecutable("LIBREOFFICE", "PREVIEW", configuration["Preview:LibreOfficePath"] ?? "libreoffice", true));
        results.Add(configuration.GetValue("Ocr:Enabled", false)
            ? CheckExecutable("OCR", "OCR", configuration["Ocr:ExecutablePath"] ?? "ocrmypdf", false)
            : Result("OCR_DISABLED", "OCR", EnvironmentCheckStatuses.NotApplicable, "OCR desabilitado", "O módulo OCR está explicitamente desabilitado.", null, false));
        results.Add(CheckHost());
        results.Add(Result("SIGNING_AGENT", "SIGNING", EnvironmentCheckStatuses.NotVerifiable, "Signing Agent", "A presença e o listener do agente exigem uma verificação no host do usuário.", "Execute o comando doctor do Signing Agent no Windows.", false));
        results.Add(Result("WORKERS", "WORKERS", EnvironmentCheckStatuses.NotVerifiable, "Workers", "Heartbeats e filas exigem acesso ao banco configurado.", "Valide os heartbeats na Central de Prontidão.", false));
        return results;
    }

    private static IEnumerable<EnvironmentCheckResult> CheckToolchain()
    {
        var selected = Run("dotnet", "--version");
        yield return selected.ExitCode == 0 && selected.Output.StartsWith("8.0.", StringComparison.Ordinal)
            ? Result("DOTNET_SDK_8_SELECTED", "TOOLCHAIN", EnvironmentCheckStatuses.Pass, ".NET SDK 8", "O SDK oficial está selecionado.", null, false, ("version", selected.Output))
            : Result("DOTNET_SDK_8_NOT_FOUND", "TOOLCHAIN", EnvironmentCheckStatuses.Fail, ".NET SDK 8 não encontrado", "Não foi possível selecionar um SDK 8 estável.", "Instale o .NET SDK 8 e execute novamente.", true, ("selectedMajor", SafeMajor(selected.Output)));
        var global = FindRepositoryFile("global.json");
        yield return global is not null && File.ReadAllText(global).Contains("\"latestFeature\"", StringComparison.Ordinal)
            ? Result("GLOBAL_JSON", "TOOLCHAIN", EnvironmentCheckStatuses.Pass, "global.json", "Baseline 8.0.100/latestFeature encontrada.", null, false)
            : Result("GLOBAL_JSON_INVALID", "TOOLCHAIN", EnvironmentCheckStatuses.Fail, "global.json inválido", "O contrato de SDK não foi encontrado.", "Restaure o global.json oficial.", true);
        yield return Result("TARGET_FRAMEWORK", "TOOLCHAIN", EnvironmentCheckStatuses.Pass, "Target framework", "Os contratos automatizados validam net8.0 em todos os projetos.", null, false, ("framework", "net8.0"));
        yield return Result("MSBUILD", "TOOLCHAIN", selected.ExitCode == 0 ? EnvironmentCheckStatuses.Pass : EnvironmentCheckStatuses.Fail, "MSBuild", "MSBuild é fornecido pelo SDK selecionado.", null, selected.ExitCode != 0);
    }

    private async Task<EnvironmentCheckResult> CheckDatabaseAsync(CancellationToken ct)
    {
        var result = await readiness("GED", ct);
        return result.Available
            ? Result("DATABASE_READY", "DATABASE", EnvironmentCheckStatuses.Pass, "PostgreSQL", "O serviço compartilhado de prontidão confirmou o banco.", null, false)
            : Result("DATABASE_UNAVAILABLE", "DATABASE", EnvironmentCheckStatuses.Warning, "PostgreSQL não verificável", "A Central de Prontidão não confirmou o módulo GED.", result.Recommendations.FirstOrDefault() ?? "Configure o banco e execute novamente.", false);
    }

    private EnvironmentCheckResult CheckStorage()
    {
        var path = configuration["Storage:Local:RootPath"];
        if (string.IsNullOrWhiteSpace(path)) return Result("STORAGE_NOT_CONFIGURED", "STORAGE", EnvironmentCheckStatuses.Warning, "Storage não configurado", "Nenhum diretório foi informado.", "Configure Storage:Local:RootPath sem divulgar o caminho em relatórios.", false);
        try { Directory.CreateDirectory(path); var probe = Path.Combine(path, $".doctor-{Guid.NewGuid():N}"); File.WriteAllText(probe, "probe"); File.Delete(probe); return Result("STORAGE_READY", "STORAGE", EnvironmentCheckStatuses.Pass, "Storage", "Leitura e gravação validadas.", null, false, ("path", MaskPath(path))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Result("STORAGE_UNAVAILABLE", "STORAGE", EnvironmentCheckStatuses.Fail, "Storage indisponível", "O diretório não permite a operação necessária.", "Revise existência, espaço e permissões.", true, ("path", MaskPath(path))); }
    }

    private static EnvironmentCheckResult CheckExecutable(string code, string category, string command, bool optional)
    { var check = Run(command, "--version"); return check.ExitCode == 0 ? Result($"{code}_READY", category, EnvironmentCheckStatuses.Pass, code, "Executável detectado.", null, false) : Result($"{code}_NOT_FOUND", category, optional ? EnvironmentCheckStatuses.Warning : EnvironmentCheckStatuses.Fail, code, "Executável não encontrado.", "Configure um executável homologado.", !optional); }
    private static EnvironmentCheckResult CheckHost() => OperatingSystem.IsWindows() ? Result("IIS_NOT_VERIFIABLE", "IIS", EnvironmentCheckStatuses.NotVerifiable, "IIS/Hosting Bundle", "A inspeção requer privilégios e contexto do host.", "Valide IIS, Hosting Bundle, binding, porta e permissões.", false) : Result("IIS_NOT_APPLICABLE", "IIS", EnvironmentCheckStatuses.NotApplicable, "IIS", "Host não Windows.", null, false);
    private static (int ExitCode, string Output) Run(string file, string args) { try { using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false, CreateNoWindow=true }); if (p is null) return (-1, ""); var output=p.StandardOutput.ReadToEnd(); p.WaitForExit(5000); return (p.ExitCode, output.Trim()); } catch { return (-1, ""); } }
    private static string? FindRepositoryFile(string name)
        => FindRepositoryFile(name, AppContext.BaseDirectory, BclEnvironment.CurrentDirectory);

    internal static string? FindRepositoryFile(string name, string baseDirectory, string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var allowedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "global.json", "InovaGed.sln", "Directory.Build.props",
            "Directory.Packages.props", "database/migrations.manifest.json"
        };
        var normalizedName = name.Replace('\\', '/');
        if (!allowedNames.Contains(normalizedName) || Path.IsPathRooted(name) || name.Contains('\0'))
            throw new ArgumentException("O nome não pertence ao catálogo seguro de arquivos do repositório.", nameof(name));

        foreach (var start in new[] { baseDirectory, currentDirectory })
        {
            if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start)) continue;
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var file = Path.Combine(current.FullName, name);
                try { if (File.Exists(file)) return file; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                current = current.Parent;
            }
        }
        return null;
    }
    private static string SafeMajor(string value) => string.IsNullOrWhiteSpace(value) ? "not-detected" : value.Split('.')[0] + ".x";
    private static string MaskPath(string path) => $"…/{Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))}";
    private static EnvironmentCheckResult Result(string code,string category,string status,string title,string message,string? recommendation,bool blocking,params (string Key,string? Value)[] metadata) => new(code,category,status,title,message,recommendation,blocking,metadata.ToDictionary(x=>x.Key,x=>x.Value));
}
