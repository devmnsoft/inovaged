using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Continuity;

public sealed class OperationsOptions { public bool WorkerEnabled { get; set; } = false; };
public sealed class BackupOptions
{
    public bool Enabled { get; set; } = false; public string RootPath { get; set; } = string.Empty; public string PostgresBinPath { get; set; } = string.Empty;
    [Range(1,16)] public int MaxParallelJobs { get; set; } = 1; [Range(1,3650)] public int DefaultRetentionDays { get; set; } = 30; public bool VerificationEnabled { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 3600; public int CompressionLevel { get; set; } = 6; public string EncryptionKeyId { get; set; } = string.Empty;
    public bool RetentionDeletionEnabled { get; set; } = false;
}
public sealed class PortabilityOptions { public bool Enabled { get; set; } = false; public string RootPath { get; set; } = string.Empty; [Range(1,365)] public int PackageExpirationDays { get; set; } = 7; [Range(1,16)] public int MaxParallelJobs { get; set; } = 1; public bool IncludeAuditByDefault { get; set; } = false; };

public sealed class BackupPolicyDto
{
    public BackupPolicyDto() { }
    public BackupPolicyDto(Guid Id, Guid? TenantId, string Name, string Scope, bool Enabled, string BackupType, string Frequency, TimeOnly? ScheduledAt, string TimeZone, int RetentionDays, string DestinationKind, bool EncryptionEnabled, bool AutoVerificationEnabled, bool AutoRestoreTestAllowed, int? RpoMinutes, int? RtoMinutes, string Status, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.Name = Name;
        this.Scope = Scope;
        this.Enabled = Enabled;
        this.BackupType = BackupType;
        this.Frequency = Frequency;
        this.ScheduledAt = ScheduledAt;
        this.TimeZone = TimeZone;
        this.RetentionDays = RetentionDays;
        this.DestinationKind = DestinationKind;
        this.EncryptionEnabled = EncryptionEnabled;
        this.AutoVerificationEnabled = AutoVerificationEnabled;
        this.AutoRestoreTestAllowed = AutoRestoreTestAllowed;
        this.RpoMinutes = RpoMinutes;
        this.RtoMinutes = RtoMinutes;
        this.Status = Status;
        this.CreatedAtUtc = CreatedAtUtc;
        this.UpdatedAtUtc = UpdatedAtUtc;
    }
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Scope { get; set; } = "";
    public bool Enabled { get; set; }
    public string BackupType { get; set; } = "";
    public string Frequency { get; set; } = "";
    public TimeOnly? ScheduledAt { get; set; }
    public string TimeZone { get; set; } = "";
    public int RetentionDays { get; set; }
    public string DestinationKind { get; set; } = "";
    public bool EncryptionEnabled { get; set; }
    public bool AutoVerificationEnabled { get; set; }
    public bool AutoRestoreTestAllowed { get; set; }
    public int? RpoMinutes { get; set; }
    public int? RtoMinutes { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
public sealed class BackupSetDto
{
    public BackupSetDto() { }
    public BackupSetDto(Guid Id, Guid? TenantId, string BackupType, DateTime StartedAtUtc, DateTime? FinishedAtUtc, string Status, long SizeBytes, long FileCount, string IntegrityStatus, string LocationMasked, bool EncryptionEnabled, string? ManifestChecksumSha256, string? CorrelationId)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.BackupType = BackupType;
        this.StartedAtUtc = StartedAtUtc;
        this.FinishedAtUtc = FinishedAtUtc;
        this.Status = Status;
        this.SizeBytes = SizeBytes;
        this.FileCount = FileCount;
        this.IntegrityStatus = IntegrityStatus;
        this.LocationMasked = LocationMasked;
        this.EncryptionEnabled = EncryptionEnabled;
        this.ManifestChecksumSha256 = ManifestChecksumSha256;
        this.CorrelationId = CorrelationId;
    }
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string BackupType { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string Status { get; set; } = "";
    public long SizeBytes { get; set; }
    public long FileCount { get; set; }
    public string IntegrityStatus { get; set; } = "";
    public string LocationMasked { get; set; } = "";
    public bool EncryptionEnabled { get; set; }
    public string? ManifestChecksumSha256 { get; set; }
    public string? CorrelationId { get; set; }
}
public sealed record ContinuityDashboardDto(DateTime GeneratedAtUtc, string OverallStatus, bool BackupEnabled, bool PortabilityEnabled, DateTime? LastCompletedBackupUtc, DateTime? LastValidBackupUtc, DateTime? LastRestoreTestUtc, string? LastRestoreTestStatus, decimal IntegrityPercent, int FailedBackups, long TotalBytesUsed, int ConfiguredRetentionDays, int? ConfiguredRpoMinutes, int? ObservedRpoMinutes, int? ConfiguredRtoMinutes, int? LastObservedRtoMinutes, int TenantsWithoutPolicy, int TenantsWithLateBackup, int ActivePortabilityPackages, int ExpiredPortabilityPackages, int RunningExports, int DeadLetterJobs, IReadOnlyList<string> Alerts);
public sealed class PortabilityExportDto
{
    public PortabilityExportDto() { }
    public PortabilityExportDto(Guid Id, Guid? TenantId, string Scope, string Status, DateTime RequestedAtUtc, DateTime? FinishedAtUtc, DateTime? ExpiresAtUtc, long SizeBytes, string? PackageSha256, string? CorrelationId)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.Scope = Scope;
        this.Status = Status;
        this.RequestedAtUtc = RequestedAtUtc;
        this.FinishedAtUtc = FinishedAtUtc;
        this.ExpiresAtUtc = ExpiresAtUtc;
        this.SizeBytes = SizeBytes;
        this.PackageSha256 = PackageSha256;
        this.CorrelationId = CorrelationId;
    }
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Scope { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public long SizeBytes { get; set; }
    public string? PackageSha256 { get; set; }
    public string? CorrelationId { get; set; }
}
public sealed class RecoveryPlanDto
{
    public RecoveryPlanDto() { }
    public RecoveryPlanDto(Guid Id, Guid? TenantId, string Name, int CurrentVersion, string Status, int? RpoMinutes, int? RtoMinutes, DateTime? LastTestAtUtc, string? LastTestResult, DateTime? NextReviewAtUtc)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.Name = Name;
        this.CurrentVersion = CurrentVersion;
        this.Status = Status;
        this.RpoMinutes = RpoMinutes;
        this.RtoMinutes = RtoMinutes;
        this.LastTestAtUtc = LastTestAtUtc;
        this.LastTestResult = LastTestResult;
        this.NextReviewAtUtc = NextReviewAtUtc;
    }
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = "";
    public int CurrentVersion { get; set; }
    public string Status { get; set; } = "";
    public int? RpoMinutes { get; set; }
    public int? RtoMinutes { get; set; }
    public DateTime? LastTestAtUtc { get; set; }
    public string? LastTestResult { get; set; }
    public DateTime? NextReviewAtUtc { get; set; }
}
public sealed class TenantOffboardingDto
{
    public TenantOffboardingDto() { }
    public TenantOffboardingDto(Guid Id, Guid TenantId, string Status, DateTime StartedAtUtc, DateTime EffectiveAtUtc, DateTime? AccessUntilUtc, bool LegalHold, string Justification)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.Status = Status;
        this.StartedAtUtc = StartedAtUtc;
        this.EffectiveAtUtc = EffectiveAtUtc;
        this.AccessUntilUtc = AccessUntilUtc;
        this.LegalHold = LegalHold;
        this.Justification = Justification;
    }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public DateTime? AccessUntilUtc { get; set; }
    public bool LegalHold { get; set; }
    public string Justification { get; set; } = "";
}
public sealed class OperationJobDto
{
    public OperationJobDto() { }
    public OperationJobDto(Guid Id, Guid? TenantId, string JobType, string Status, int ProgressPercent, string? CurrentStep, DateTime CreatedAtUtc, DateTime? LockedUntilUtc, string? CorrelationId)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.JobType = JobType;
        this.Status = Status;
        this.ProgressPercent = ProgressPercent;
        this.CurrentStep = CurrentStep;
        this.CreatedAtUtc = CreatedAtUtc;
        this.LockedUntilUtc = LockedUntilUtc;
        this.CorrelationId = CorrelationId;
    }
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string JobType { get; set; } = "";
    public string Status { get; set; } = "";
    public int ProgressPercent { get; set; }
    public string? CurrentStep { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? CorrelationId { get; set; }
}

public interface IBackupPolicyService { Task<IReadOnlyList<BackupPolicyDto>> ListAsync(Guid? tenantId, CancellationToken ct); Task<BackupPolicyDto> SaveAsync(BackupPolicyDto policy, string userName, string justification, CancellationToken ct); }
public interface IBackupOrchestrator { Task<OperationJobDto> EnqueueBackupAsync(Guid? tenantId, Guid? policyId, string requestedBy, string correlationId, CancellationToken ct); Task<int> ProcessDueJobsAsync(string workerId, CancellationToken ct); }
public interface IBackupCatalogService { Task<IReadOnlyList<BackupSetDto>> ListAsync(Guid? tenantId, string? status, CancellationToken ct); Task<BackupSetDto?> GetAsync(Guid id, Guid? tenantId, CancellationToken ct); }
public interface IBackupIntegrityService { Task<BackupVerificationResult> VerifyAsync(Guid backupSetId, string workerId, CancellationToken ct); }
public interface IBackupRetentionService { Task<int> MarkExpiredAsync(Guid? tenantId, string requestedBy, CancellationToken ct); }
public sealed record BackupVerificationResult(Guid BackupSetId, string Status, IReadOnlyList<string> Findings, DateTime VerifiedAtUtc);
public sealed record BackupManifest(string FormatVersion, Guid BackupSetId, Guid? TenantId, DateTime StartedAtUtc, DateTime CompletedAtUtc, string Application, string Schema, string PostgreSqlVersion, string PgDumpVersion, string BackupType, string ConsistencyMode, string Status, string CorrelationId, IReadOnlyList<BackupManifestArtifact> Artifacts);
public sealed record BackupManifestArtifact(string Path, string Sha256, long SizeBytes);
public interface IRestoreValidationService { Task<RestoreValidationResult> ValidateTargetAsync(string host, string database, bool confirmed, string justification, CancellationToken ct); }
public sealed record RestoreValidationResult(bool Allowed, string Reason);
public interface IRecoveryPlanService { Task<IReadOnlyList<RecoveryPlanDto>> ListAsync(Guid? tenantId, CancellationToken ct); }
public interface IPortabilityExportService { Task<PortabilityExportDto> RequestAsync(Guid? tenantId, string scope, string requestedBy, string idempotencyKey, string correlationId, CancellationToken ct); Task<PortabilityExportDto?> GetAsync(Guid id, Guid? tenantId, CancellationToken ct); Task<bool> CancelAsync(Guid id, Guid? tenantId, string requestedBy, CancellationToken ct); }
public interface IPortabilityManifestService { Task<PortabilityManifest> BuildAsync(Guid exportId, CancellationToken ct); }
public interface IPortabilityPackageVerifier { Task<PortabilityVerificationResult> VerifyAsync(string packagePath, CancellationToken ct); }
public sealed record PortabilityManifest(string FormatVersion, Guid ExportId, Guid? TenantId, string Scope, DateTime CreatedAtUtc, string State, string CorrelationId, IReadOnlyList<PortabilityManifestFile> Files);
public sealed class PortabilityManifestFile
{
    public PortabilityManifestFile() { }
    public PortabilityManifestFile(string Path, long SizeBytes, string Sha256)
    {
        this.Path = Path;
        this.SizeBytes = SizeBytes;
        this.Sha256 = Sha256;
    }
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
}
public sealed record PortabilityVerificationResult(bool Valid, IReadOnlyList<string> Findings);
public interface ITenantOffboardingService { Task<IReadOnlyList<TenantOffboardingDto>> ListAsync(Guid? tenantId, CancellationToken ct); Task<TenantOffboardingDto> StartAsync(Guid tenantId, DateTime effectiveAtUtc, string justification, string requestedBy, CancellationToken ct); Task<RestoreValidationResult> ApproveDeletionAsync(Guid offboardingId, string justification, CancellationToken ct); }
public interface IDataDeletionWorkflowService { Task<bool> IsDeletionBlockedAsync(Guid tenantId, CancellationToken ct); }
public interface IRecoveryObjectiveService { Task<ContinuityDashboardDto> GetDashboardAsync(Guid? tenantId, CancellationToken ct); }
