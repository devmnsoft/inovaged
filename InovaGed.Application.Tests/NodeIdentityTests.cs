using InovaGed.Application.Cluster;
using InovaGed.Infrastructure.Cluster;
using Microsoft.Extensions.Options;

namespace InovaGed.Application.Tests;

public sealed class NodeIdentityTests
{
    [Fact]
    public void Configured_identity_is_stable_and_contains_no_network_address()
    {
        var identity = new NodeIdentity(Options.Create(new NodeIdentityOptions
        {
            ClusterId = "cluster-a", NodeId = "Blue Node 01", NodeName = "Web 01", Color = NodeColor.Blue
        }));

        Assert.Equal("blue-node-01", identity.NodeId);
        Assert.Equal("cluster-a", identity.ClusterId);
        Assert.Equal("Blue", identity.Color);
        Assert.DoesNotContain('.', identity.NodeId);
    }

    [Fact]
    public void Fallback_identity_is_unique_per_instance_and_bounded()
    {
        var options = Options.Create(new NodeIdentityOptions());
        var first = new NodeIdentity(options);
        var second = new NodeIdentity(options);

        Assert.NotEqual(first.NodeId, second.NodeId);
        Assert.InRange(first.NodeId.Length, 8, 63);
        Assert.Matches("^[a-z0-9-]+$", first.NodeId);
    }
}
