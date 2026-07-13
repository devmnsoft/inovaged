namespace InovaGed.Application.Manifest;
public sealed record IntegrityManifest(string EntityType, Guid EntityId, Guid TenantId, DateTime ExportedAtUtc, Guid ExportedBy, IReadOnlyList<IntegrityManifestFile> Files, IReadOnlyDictionary<string,string?> Metadata);
public sealed record IntegrityManifestFile(Guid DocumentId, Guid VersionId, string FileName, string HashSha256, DateTime CreatedAtUtc);
public interface IIntegrityManifestService { Task<IntegrityManifest> BuildAsync(Guid tenantId, string entityType, Guid entityId, Guid exportedBy, CancellationToken ct); }
