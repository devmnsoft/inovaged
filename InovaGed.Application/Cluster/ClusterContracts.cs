namespace InovaGed.Application.Cluster;

public enum ClusterMode { SingleNode, MultiNode, BlueGreen }
public enum NodeColor { Standalone, Blue, Green }
public enum ClusterNodeStatus { Starting, Warming, Ready, Draining, Drained, Unhealthy, Stopping, Offline }

public sealed class NodeIdentityOptions
{
    public ClusterMode Mode { get; set; } = ClusterMode.SingleNode;
    public string ClusterId { get; set; } = "inovaged-production";
    public string NodeId { get; set; } = "";
    public string NodeName { get; set; } = "";
    public NodeColor Color { get; set; } = NodeColor.Standalone;
    public string Region { get; set; } = "local";
    public string AvailabilityZone { get; set; } = "default";
    public int HeartbeatSeconds { get; set; } = 15;
    public int NodeExpirationSeconds { get; set; } = 60;
    public bool RequireDistributedDependencies { get; set; }
}

public interface INodeIdentity
{
    string ClusterId { get; }
    string NodeId { get; }
    string NodeName { get; }
    string Color { get; }
    string Region { get; }
    string AvailabilityZone { get; }
    string ApplicationVersion { get; }
    string CommitSha { get; }
    DateTimeOffset StartedAtUtc { get; }
}

public sealed record ClusterNodeHeartbeat(ClusterNodeStatus Status, DateTimeOffset TimestampUtc, string HealthSummary);
public interface IClusterNodeRegistry
{
    Task RegisterAsync(INodeIdentity node, ClusterNodeHeartbeat heartbeat, CancellationToken cancellationToken);
    Task HeartbeatAsync(INodeIdentity node, ClusterNodeHeartbeat heartbeat, CancellationToken cancellationToken);
}

public sealed record ClusterLease(string Name, string ClusterId, string OwnerNodeId, long FencingToken, DateTimeOffset ExpiresAtUtc);
public interface IClusterLeaseManager
{
    Task<ClusterLease?> TryAcquireAsync(string name, TimeSpan duration, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ClusterLease lease, TimeSpan duration, CancellationToken cancellationToken);
    Task ReleaseAsync(ClusterLease lease, CancellationToken cancellationToken);
    Task<bool> ValidateFencingTokenAsync(ClusterLease lease, CancellationToken cancellationToken);
}
