using System.Text.Json; using InovaGed.Deployment;
if(args.Length!=3||args[0]!="verify-package"||args[1]!="--package") { Console.Error.WriteLine("Uso: verify-package --package <arquivo.zip>"); return 3; }
var report=PackageVerifier.Verify(args[2]); Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase})); return report.Valid?0:2;
