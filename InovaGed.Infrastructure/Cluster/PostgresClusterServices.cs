using Dapper;
using InovaGed.Application.Cluster;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Cluster;

public sealed class PostgresClusterNodeRegistry(IDbConnectionFactory db) : IClusterNodeRegistry
{
    public Task RegisterAsync(INodeIdentity node, ClusterNodeHeartbeat heartbeat, CancellationToken ct) => UpsertAsync(node, heartbeat, ct);
    public Task HeartbeatAsync(INodeIdentity node, ClusterNodeHeartbeat heartbeat, CancellationToken ct) => UpsertAsync(node, heartbeat, ct);
    private async Task UpsertAsync(INodeIdentity n, ClusterNodeHeartbeat h, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        const string sql = """INSERT INTO ged.cluster_node(cluster_id,node_id,node_name,color,region,availability_zone,application_version,commit_sha,status,started_at_utc,last_heartbeat_at_utc,registered_at_utc,metadata_json) VALUES (@ClusterId,@NodeId,@NodeName,@Color,@Region,@AvailabilityZone,@ApplicationVersion,@CommitSha,@Status,@StartedAtUtc,@TimestampUtc,now(),'{}') ON CONFLICT(cluster_id,node_id) DO UPDATE SET status=excluded.status,last_heartbeat_at_utc=excluded.last_heartbeat_at_utc,application_version=excluded.application_version,commit_sha=excluded.commit_sha; INSERT INTO ged.cluster_node_heartbeat(cluster_id,node_id,status,heartbeat_at_utc,health_json) VALUES (@ClusterId,@NodeId,@Status,@TimestampUtc,jsonb_build_object('summary',@HealthSummary));""";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { n.ClusterId,n.NodeId,n.NodeName,n.Color,n.Region,n.AvailabilityZone,n.ApplicationVersion,n.CommitSha, Status=h.Status.ToString().ToUpperInvariant(),n.StartedAtUtc,h.TimestampUtc,h.HealthSummary }, cancellationToken:ct));
    }
}

public sealed class PostgresClusterLeaseManager(IDbConnectionFactory db, INodeIdentity node) : IClusterLeaseManager
{
    public async Task<ClusterLease?> TryAcquireAsync(string name, TimeSpan duration, CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct); var expires=DateTimeOffset.UtcNow.Add(duration);
        const string sql="""INSERT INTO ged.cluster_leader_lease(lease_name,cluster_id,owner_node_id,acquired_at_utc,renewed_at_utc,expires_at_utc,fencing_token,metadata_json) VALUES(@name,@cluster,@owner,now(),now(),@expires,1,'{}') ON CONFLICT(lease_name,cluster_id) DO UPDATE SET owner_node_id=@owner,acquired_at_utc=now(),renewed_at_utc=now(),expires_at_utc=@expires,fencing_token=ged.cluster_leader_lease.fencing_token+1 WHERE ged.cluster_leader_lease.expires_at_utc<now() OR ged.cluster_leader_lease.owner_node_id=@owner RETURNING fencing_token""";
        var token=await c.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(sql,new{name,cluster=node.ClusterId,owner=node.NodeId,expires},cancellationToken:ct));
        return token is null?null:new(name,node.ClusterId,node.NodeId,token.Value,expires);
    }
    public async Task<bool> RenewAsync(ClusterLease l,TimeSpan duration,CancellationToken ct){await using var c=await db.OpenAsync(ct);return await c.ExecuteAsync(new CommandDefinition("UPDATE ged.cluster_leader_lease SET renewed_at_utc=now(),expires_at_utc=now()+@duration WHERE lease_name=@Name AND cluster_id=@ClusterId AND owner_node_id=@OwnerNodeId AND fencing_token=@FencingToken AND expires_at_utc>now()",new{l.Name,l.ClusterId,l.OwnerNodeId,l.FencingToken,duration},cancellationToken:ct))==1;}
    public async Task ReleaseAsync(ClusterLease l,CancellationToken ct){await using var c=await db.OpenAsync(ct);await c.ExecuteAsync(new CommandDefinition("UPDATE ged.cluster_leader_lease SET expires_at_utc=now() WHERE lease_name=@Name AND cluster_id=@ClusterId AND owner_node_id=@OwnerNodeId AND fencing_token=@FencingToken",l,cancellationToken:ct));}
    public async Task<bool> ValidateFencingTokenAsync(ClusterLease l,CancellationToken ct){await using var c=await db.OpenAsync(ct);return await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM ged.cluster_leader_lease WHERE lease_name=@Name AND cluster_id=@ClusterId AND owner_node_id=@OwnerNodeId AND fencing_token=@FencingToken AND expires_at_utc>now())",l,cancellationToken:ct));}
}
