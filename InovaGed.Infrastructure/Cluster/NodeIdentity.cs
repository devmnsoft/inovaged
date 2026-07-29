using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using InovaGed.Application.Cluster;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Cluster;

public sealed class NodeIdentity : INodeIdentity
{
    public NodeIdentity(IOptions<NodeIdentityOptions> options)
    {
        var value = options.Value;
        ClusterId = value.ClusterId;
        NodeName = string.IsNullOrWhiteSpace(value.NodeName) ? "inovaged-node" : value.NodeName.Trim();
        NodeId = string.IsNullOrWhiteSpace(value.NodeId) ? CreateFallbackId() : Normalize(value.NodeId);
        Color = value.Color.ToString(); Region = value.Region; AvailabilityZone = value.AvailabilityZone;
        var assembly = Assembly.GetEntryAssembly();
        ApplicationVersion = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
        CommitSha = Environment.GetEnvironmentVariable("INOVAGED_COMMIT_SHA")?.Trim() ?? "unknown";
        StartedAtUtc = DateTimeOffset.UtcNow;
    }
    public string ClusterId { get; } public string NodeId { get; } public string NodeName { get; }
    public string Color { get; } public string Region { get; } public string AvailabilityZone { get; }
    public string ApplicationVersion { get; } public string CommitSha { get; } public DateTimeOffset StartedAtUtc { get; }
    private static string CreateFallbackId()
    {
        var id = $"{Normalize(Environment.MachineName)}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        return id[..Math.Min(63, id.Length)];
    }
    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9-]", "-").Trim('-');
}
