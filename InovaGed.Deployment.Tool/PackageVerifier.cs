using System.IO.Compression; using System.Security.Cryptography; using System.Text; using System.Text.Json; using System.Text.RegularExpressions;
namespace InovaGed.Deployment;
public sealed record VerificationIssue(string Code,string Path,string Message);
public sealed record VerificationReport(bool Valid,IReadOnlyList<VerificationIssue> Issues);
public static class PackageVerifier
{
 static readonly string[] Required=["app/InovaGed.Web.dll","app/web.config","tools/doctor/InovaGed.Environment.Doctor.dll","tools/migrator/InovaGed.Database.Migrator.dll","database/migrations.manifest.json","config/appsettings.Production.example.json","config/deployment.example.json","deployment/Invoke-InovaGedDeployment.ps1","manifest/release-manifest.json","manifest/build-information.json","checksums.sha256"];
 static readonly string[] TextExtensions=[".json",".config",".xml",".ps1",".cmd",".bat",".sh",".txt",".md"];
 static readonly Regex Secret=new(@"(?im)(Password|Pwd)\s*=\s*(?!(SUA_SENHA|YOUR_PASSWORD|\$\{SECRET\}|<SET_EXTERNALLY>))\S+|Bearer\s+\S+|BEGIN (RSA )?PRIVATE KEY|BEGIN CERTIFICATE|client_secret\s*[:=]|api_key\s*[:=]|token\s*=",RegexOptions.NonBacktracking);
 public static VerificationReport Verify(string packagePath)
 {
  var issues=new List<VerificationIssue>(); if(!File.Exists(packagePath)) return new(false,[new("PACKAGE_NOT_FOUND",packagePath,"Pacote não encontrado.")]);
  try { using var zip=ZipFile.OpenRead(packagePath); var files=zip.Entries.Where(e=>!string.IsNullOrEmpty(e.Name)).ToDictionary(e=>Normalize(e.FullName),StringComparer.OrdinalIgnoreCase);
   foreach(var path in Required) if(!files.ContainsKey(path)) issues.Add(new("REQUIRED_FILE_MISSING",path,"Arquivo obrigatório ausente."));
   if(files.Keys.Any(p=>p.Contains("..",StringComparison.Ordinal)||Path.IsPathRooted(p))) issues.Add(new("UNSAFE_PATH","package","Caminho inseguro no ZIP."));
   if(files.TryGetValue("checksums.sha256",out var checksumEntry)) VerifyChecksums(files,checksumEntry,issues);
   foreach(var (path,entry) in files) { if(IsForbidden(path)) issues.Add(new("FORBIDDEN_FILE",path,"Conteúdo proibido no pacote.")); if(TextExtensions.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase)) { using var reader=new StreamReader(entry.Open(),Encoding.UTF8,true); var text=reader.ReadToEnd(); if(Secret.IsMatch(text)) issues.Add(new("SECRET_PATTERN",path,"Possível segredo encontrado.")); } }
   if(files.TryGetValue("manifest/release-manifest.json",out var manifestEntry)) { using var s=manifestEntry.Open(); var m=JsonSerializer.Deserialize<ReleaseManifest>(s,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}); if(m is null||m.SchemaVersion!="1.0"||m.Product!="InovaGED"||string.IsNullOrWhiteSpace(m.CommitSha)) issues.Add(new("MANIFEST_INVALID","manifest/release-manifest.json","Manifesto inválido ou incompleto.")); }
  } catch(InvalidDataException ex) { issues.Add(new("PACKAGE_TRUNCATED",packagePath,ex.Message)); } catch(JsonException ex) { issues.Add(new("MANIFEST_INVALID","manifest/release-manifest.json",ex.Message)); }
  return new(issues.Count==0,issues);
 }
 static void VerifyChecksums(Dictionary<string,ZipArchiveEntry> files,ZipArchiveEntry entry,List<VerificationIssue> issues) { using var reader=new StreamReader(entry.Open()); var declared=new HashSet<string>(StringComparer.OrdinalIgnoreCase); string? line; while((line=reader.ReadLine()) is not null) { var parts=line.Split("  ",2); if(parts.Length!=2||parts[0].Length!=64){issues.Add(new("CHECKSUM_FORMAT","checksums.sha256","Linha inválida."));continue;} var path=Normalize(parts[1]); declared.Add(path); if(!files.TryGetValue(path,out var target)){issues.Add(new("CHECKSUM_FILE_MISSING",path,"Arquivo declarado ausente."));continue;} using var stream=target.Open(); var actual=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); if(!actual.Equals(parts[0],StringComparison.OrdinalIgnoreCase))issues.Add(new("CHECKSUM_MISMATCH",path,"SHA-256 divergente.")); } foreach(var path in files.Keys.Where(p=>p!="checksums.sha256"&&!declared.Contains(p)))issues.Add(new("EXTRA_FILE",path,"Arquivo sem checksum.")); }
 static string Normalize(string path)=>path.Replace('\\','/').TrimStart('/');
 static bool IsForbidden(string path)=>path.EndsWith(".pfx",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".key",StringComparison.OrdinalIgnoreCase)||path.Contains("usersecrets",StringComparison.OrdinalIgnoreCase)||path.StartsWith("storage/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("logs/",StringComparison.OrdinalIgnoreCase)||path.Contains("appsettings.Production.local",StringComparison.OrdinalIgnoreCase);
}
