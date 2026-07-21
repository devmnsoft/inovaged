using System.Security.Cryptography;
using System.Text.Json;
if (args.Length == 0) { Console.Error.WriteLine("Uso: InovaGed.Portability.Verifier <pasta-do-pacote>"); return 2; }
var root = Path.GetFullPath(args[0]); var manifest = Path.Combine(root, "manifest.json"); var checksums = Path.Combine(root, "checksums.sha256"); var findings = new List<string>();
if (!File.Exists(manifest)) findings.Add("manifest.json ausente"); if (!File.Exists(checksums)) findings.Add("checksums.sha256 ausente");
if (File.Exists(checksums)) foreach (var line in File.ReadLines(checksums)) { var parts=line.Split(' ', StringSplitOptions.RemoveEmptyEntries); if(parts.Length<2) continue; var rel=parts[^1].Replace('/', Path.DirectorySeparatorChar); var file=Path.GetFullPath(Path.Combine(root, rel)); if(!file.StartsWith(root)) { findings.Add($"path traversal: {rel}"); continue; } if(!File.Exists(file)) { findings.Add($"ausente: {rel}"); continue; } await using var fs=File.OpenRead(file); var hash=Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant(); if(hash!=parts[0].ToLowerInvariant()) findings.Add($"alterado: {rel}"); }
var result = new { valid = findings.Count == 0, findings }; Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions{WriteIndented=true})); return findings.Count == 0 ? 0 : 1;
