using System.Text.Json;
using InovaGed.Application.Continuity;
using InovaGed.Infrastructure.Continuity;

namespace InovaGed.Application.Tests;

public sealed class ContinuityHardeningTests
{
 [Fact] public void Rpo_is_nullable_utc_and_never_negative(){var now=DateTime.UtcNow;Assert.Null(RecoveryObjectiveService.CalculateObservedRpoMinutes(null,now));Assert.InRange(RecoveryObjectiveService.CalculateObservedRpoMinutes(now.AddMinutes(-2),now)!.Value,1,2);Assert.Equal(0,RecoveryObjectiveService.CalculateObservedRpoMinutes(now.AddMinutes(10),now));Assert.Equal(1440,RecoveryObjectiveService.CalculateObservedRpoMinutes(now.AddDays(-1),now));}
 [Fact] public void Backup_manifest_is_typed_relative_and_contains_no_secret_contract(){var manifest=new BackupManifest("1.0",Guid.NewGuid(),null,DateTime.UtcNow,DateTime.UtcNow,"InovaGED","ged","16","16","POSTGRESQL","custom","COMPLETED","correlation",[new("database.dump","ab",12)]);var json=JsonSerializer.Serialize(manifest);Assert.Contains("database.dump",json);Assert.DoesNotContain("connectionString",json,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("password",json,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("token",json,StringComparison.OrdinalIgnoreCase);}
 [Theory] [InlineData("PENDING","CLAIMED",true)] [InlineData("CLAIMED","RUNNING",true)] [InlineData("RUNNING","VERIFYING",true)] [InlineData("VERIFYING","COMPLETED",true)] [InlineData("COMPLETED","RUNNING",false)] [InlineData("FAILED","RETRY",false)] public void Job_state_machine_rejects_invalid_and_terminal_transitions(string from,string to,bool allowed)=>Assert.Equal(allowed,BackupJobStatuses.CanTransition(from,to));
 [Fact] public void Backup_and_portability_get_contracts_resolve_to_distinct_types(){Assert.Contains(typeof(IBackupCatalogService),typeof(BackupCatalogRepository).GetInterfaces());Assert.Contains(typeof(IPortabilityExportService),typeof(PortabilityExportRepository).GetInterfaces());Assert.NotEqual(typeof(BackupCatalogRepository),typeof(PortabilityExportRepository));}
 [Fact] public void Backup_root_requires_absolute_non_public_path(){Assert.Throws<InvalidOperationException>(()=>BackupPathSecurity.CreateSetDirectory("../backup",null,Guid.NewGuid()));Assert.Throws<InvalidOperationException>(()=>BackupPathSecurity.CreateSetDirectory(Path.Combine(Path.GetTempPath(),"wwwroot","backup"),null,Guid.NewGuid()));}
}
