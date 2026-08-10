using arch_forge_backend.Domain.Entities;
using Xunit;

namespace arch_forge_backend.Domain.Tests.Entities;

/// <summary>
/// V1's domain is deliberately anemic (Technical_Design_V1 §3.2: behaviour lives in Application,
/// the entities are the persistence shape). What is worth pinning here is the state a freshly
/// constructed entity carries, because Infrastructure and Application both rely on it: the
/// concurrency protocol starts at version 1, the graph collections are never null, and a node
/// without configuration still holds a JSON object rather than null (§5.1, jsonb not null).
/// </summary>
public class ArchitectureDefaultsTests
{
    [Fact]
    public void NewArchitecture_StartsAtVersion1()
    {
        // §3.7: POST returns version 1 and the first PUT has to send that same number back.
        Assert.Equal(1, new Architecture().Version);
    }

    [Fact]
    public void NewArchitecture_HasEmptyGraphCollections()
    {
        var architecture = new Architecture();

        Assert.Empty(architecture.Nodes);
        Assert.Empty(architecture.Connections);
    }

    [Fact]
    public void NewNode_HasEmptyJsonObjectConfiguration()
    {
        // §5.1: Nodes.Configuration is jsonb NOT NULL, so "nothing configured" is {}, not null.
        Assert.Equal("{}", new Node().Configuration);
        Assert.Equal("{}", Node.EmptyConfiguration);
    }

    [Fact]
    public void NewConnection_HasEmptyJsonObjectConfiguration()
    {
        // V2 D3.1: resilience lives inside a jsonb object, symmetric to the node's, so an edge
        // with nothing configured carries {} rather than NULL.
        Assert.Equal("{}", new Connection().Configuration);
        Assert.Equal("{}", Connection.EmptyConfiguration);
    }
}
