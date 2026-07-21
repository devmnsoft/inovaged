using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Continuity;

public sealed class OperationsOptions { public bool WorkerEnabled { get; set; } = false; }
public sealed class BackupOptions
{
    public bool Enabled { get; set; } = false; public string RootPath { get; set; } = string.Empty; public string PostgresBinPath { get; set; } = string.Empty;
    [Range(1,16)] public int MaxParallelJobs { get; set; } = 1; [Range(1,3650)] public int DefaultRetentionDays { get; set; } = 30; public bool VerificationEnabled { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 3600; public int CompressionLevel { get; set; } = 6; public string EncryptionKeyId { get; set; } = string.Empty;
}
public sealed class PortabilityOptions { public bool Enabled { get; set; } = false; public string RootPath { get; set; } = string.Empty; [Range(1,365)] public int PackageExpirationDays { get; set; } = 7; [Range(1,16)] public int MaxParallelJobs { get; set; } = 1; public bool IncludeAuditByDefault { get; set; } = false; }

public sealed record BackupPolicyDto(Guid Id, Guid? TenantId, string Name, string Scope, bool Enabled, string BackupType, string Frequency, TimeOnly? ScheduledAt, string TimeZone, int RetentionDays, string DestinationKind, bool EncryptionEnabled, bool AutoVerificationEnabled, bool AutoRestoreTestAllowed, int? RpoMinutes, int? RtoMinutes, string Status, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);
public sealed record BackupSetDto(Guid Id, Guid? TenantId, string BackupType, DateTime StartedAtUtc, DateTime? FinishedAtUtc, string Status, long SizeBytes, long FileCount, string IntegrityStatus, string LocationMasked, bool EncryptionEnabled, string? ManifestChecksumSha256, string? CorrelationId);
public sealed record ContinuityDashboardDto(DateTime GeneratedAtUtc, string OverallStatus, bool BackupEnabled, bool PortabilityEnabled, DateTime? LastCompletedBackupUtc, DateTime? LastValidBackupUtc, DateTime? LastRestoreTestUtc, string? LastRestoreTestStatus, decimal IntegrityPercent, int FailedBackups, long TotalBytesUsed, int ConfiguredRetentionDays, int? ConfiguredRpoMinutes, int? ObservedRpoMinutes, int? ConfiguredRtoMinutes, int? LastObservedRtoMinutes, int TenantsWithoutPolicy, int TenantsWithLateBackup, int ActivePortabilityPackages, int ExpiredPortabilityPackages, int RunningExports, int DeadLetterJobs, IReadOnlyList<string> Alerts);
public sealed record PortabilityExportDto(Guid Id, Guid? TenantId, string Scope, string Status, DateTime RequestedAtUtc, DateTime? FinishedAtUtc, DateTime? ExpiresAtUtc, long SizeBytes, string? PackageSha256, string? CorrelationId);
public sealed record RecoveryPlanDto(Guid Id, Guid? TenantId, string Name, int CurrentVersion, string Status, int? RpoMinutes, int? RtoMinutes, DateTime? LastTestAtUtc, string? LastTestResult, DateTime? NextReviewAtUtc);
public sealed record TenantOffboardingDto(Guid Id, Guid TenantId, string Status, DateTime StartedAtUtc, DateTime EffectiveAtUtc, DateTime? AccessUntilUtc, bool LegalHold, string Justification);
public sealed record OperationJobDto(Guid Id, Guid? TenantId, string JobType, string Status, int ProgressPercent, string? CurrentStep, DateTime CreatedAtUtc, DateTime? LockedUntilUtc, string? CorrelationId);

public interface IBackupPolicyService { Task<IReadOnlyList<BackupPolicyDto>> ListAsync(Guid? tenantId, CancellationToken ct); Task<BackupPolicyDto> SaveAsync(BackupPolicyDto policy, string userName, string justification, CancellationToken ct); }
public interface IBackupOrchestrator { Task<OperationJobDto> EnqueueBackupAsync(Guid? tenantId, Guid? policyId, string requestedBy, string correlationId, CancellationToken ct); Task<int> ProcessDueJobsAsync(string workerId, CancellationToken ct); }
public interface IBackupCatalogService { Task<IReadOnlyList<BackupSetDto>> ListAsync(Guid? tenantId, string? status, CancellationToken ct); Task<BackupSetDto?> GetAsync(Guid id, Guid? tenantId, CancellationToken ct); }
public interface IBackupIntegrityService { Task<BackupVerificationResult> VerifyAsync(Guid backupSetId, string workerId, CancellationToken ct); }
public sealed record BackupVerificationResult(Guid BackupSetId, string Status, IReadOnlyList<string> Findings, DateTime VerifiedAtUtc);
public interface IRestoreValidationService { Task<RestoreValidationResult> ValidateTargetAsync(string host, string database, bool confirmed, string justification, CancellationToken ct); }
public sealed record RestoreValidationResult(bool Allowed, string Reason);
public interface IRecoveryPlanService { Task<IReadOnlyList<RecoveryPlanDto>> ListAsync(Guid? tenantId, CancellationToken ct); }
public interface IPortabilityExportService { Task<PortabilityExportDto> RequestAsync(Guid? tenantId, string scope, string requestedBy, string idempotencyKey, string correlationId, CancellationToken ct); Task<PortabilityExportDto?> GetAsync(Guid id, Guid? tenantId, CancellationToken ct); Task<bool> CancelAsync(Guid id, Guid? tenantId, string requestedBy, CancellationToken ct); }
public interface IPortabilityManifestService { Task<PortabilityManifest> BuildAsync(Guid exportId, CancellationToken ct); }
public interface IPortabilityPackageVerifier { Task<PortabilityVerificationResult> VerifyAsync(string packagePath, CancellationToken ct); }
public sealed record PortabilityManifest(string FormatVersion, Guid ExportId, Guid? TenantId, string Scope, DateTime CreatedAtUtc, string State, string CorrelationId, IReadOnlyList<PortabilityManifestFile> Files);
public sealed record PortabilityManifestFile(string Path, long SizeBytes, string Sha256);
public sealed record PortabilityVerificationResult(bool Valid, IReadOnlyList<string> Findings);
public interface ITenantOffboardingService { Task<IReadOnlyList<TenantOffboardingDto>> ListAsync(Guid? tenantId, CancellationToken ct); Task<TenantOffboardingDto> StartAsync(Guid tenantId, DateTime effectiveAtUtc, string justification, string requestedBy, CancellationToken ct); Task<RestoreValidationResult> ApproveDeletionAsync(Guid offboardingId, string justification, CancellationToken ct); }
public interface IDataDeletionWorkflowService { Task<bool> IsDeletionBlockedAsync(Guid tenantId, CancellationToken ct); }
public interface IRecoveryObjectiveService { Task<ContinuityDashboardDto> GetDashboardAsync(Guid? tenantId, CancellationToken ct); }
