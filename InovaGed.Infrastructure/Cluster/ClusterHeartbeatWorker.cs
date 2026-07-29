using InovaGed.Application.Cluster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Cluster;
public sealed class ClusterHeartbeatWorker(IClusterNodeRegistry registry,INodeIdentity node,IOptions<NodeIdentityOptions> options,ILogger<ClusterHeartbeatWorker> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct){var delay=TimeSpan.FromSeconds(Math.Clamp(options.Value.HeartbeatSeconds,5,300));var failures=0;while(!ct.IsCancellationRequested){try{await registry.HeartbeatAsync(node,new(ClusterNodeStatus.Ready,DateTimeOffset.UtcNow,"ready"),ct);failures=0;}catch(Exception ex) when(!ct.IsCancellationRequested){failures++;if(failures==1||failures%10==0)logger.LogWarning(ex,"Cluster heartbeat failed for node {NodeId}; consecutive failures {Failures}",node.NodeId,failures);}await Task.Delay(delay,ct);}}
}
