using bondstone_backend.Domain.Entities;
using Xunit;

namespace bondstone_backend.Domain.Tests.Entities;

public class ArchitectureDefaultsTests
{
    [Fact]
    public void NewArchitecture_StartsAtVersion1()
    {
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
        Assert.Equal("{}", new Node().Configuration);
        Assert.Equal("{}", Node.EmptyConfiguration);
    }

    [Fact]
    public void NewConnection_HasEmptyJsonObjectConfiguration()
    {
        Assert.Equal("{}", new Connection().Configuration);
        Assert.Equal("{}", Connection.EmptyConfiguration);
    }
}
