using System.Security.Cryptography;
using System.Text.Json;

var findings = new List<string>();
if (args.Length == 0)
{
    Console.Error.WriteLine("Uso: InovaGed.Portability.Verifier <pasta-do-pacote>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root)) findings.Add("diretório raiz ausente");
root = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
var manifest = Path.Combine(root, "manifest.json");
var checksums = Path.Combine(root, "checksums.sha256");
if (!File.Exists(manifest)) findings.Add("manifest.json ausente");
if (!File.Exists(checksums)) findings.Add("checksums.sha256 ausente");

var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
if (File.Exists(checksums))
{
    foreach (var (line, number) in File.ReadLines(checksums).Select((l, i) => (l, i + 1)))
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) { findings.Add($"linha inválida em checksums.sha256:{number}"); continue; }
        var rel = parts[^1].Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(rel)) { findings.Add($"caminho absoluto bloqueado: {rel}"); continue; }
        var file = Path.GetFullPath(Path.Combine(root, rel));
        if (!file.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            findings.Add($"path traversal: {rel}");
            continue;
        }
        if (!referenced.Add(file)) findings.Add($"entrada duplicada: {rel}");
        if (!File.Exists(file)) { findings.Add($"ausente: {rel}"); continue; }
        var attributes = File.GetAttributes(file);
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) { findings.Add($"link simbólico/reparse point bloqueado: {rel}"); continue; }
        await using var fs = File.OpenRead(file);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
        if (hash != parts[0].ToLowerInvariant()) findings.Add($"alterado: {rel}");
    }
}

foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(Path.GetFullPath))
{
    if (file == manifest || file == checksums) continue;
    if (!referenced.Contains(file)) findings.Add($"arquivo extra: {Path.GetRelativePath(root, file)}");
}

var result = new { valid = findings.Count == 0, summary = findings.Count == 0 ? "válido" : "inválido", findings };
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return findings.Count == 0 ? 0 : 1;
