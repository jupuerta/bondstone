using bondstone_backend.Application.Simulation;
using bondstone_backend.Domain.Entities;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Simulation;

public class NodeSettingsZoneTests
{
    [Fact]
    public void A_declared_zone_is_read_from_the_configuration()
    {
        Assert.Equal("b", NodeSettings.Zone(Node(NodeType.Api, """{"zone":"b"}""")));
    }

    [Fact]
    public void The_key_is_case_insensitive_like_its_sisters()
    {
        Assert.Equal("c", NodeSettings.Zone(Node(NodeType.Service, """{"Zone":"c"}""")));
    }

    [Fact]
    public void Without_a_declaration_there_is_no_zone()
    {
        Assert.Null(NodeSettings.Zone(Node(NodeType.Api, "{}")));
        Assert.Null(NodeSettings.Zone(Node(NodeType.Api, "")));
    }

    [Fact]
    public void A_value_outside_the_closed_set_means_no_zone()
    {
        Assert.Null(NodeSettings.Zone(Node(NodeType.Api, """{"zone":"d"}""")));
        Assert.Null(NodeSettings.Zone(Node(NodeType.Api, """{"zone":"A"}""")));
        Assert.Null(NodeSettings.Zone(Node(NodeType.Api, """{"zone":3}""")));
    }

    [Fact]
    public void A_type_that_is_not_yours_to_place_never_has_a_zone()
    {
        Assert.Null(NodeSettings.Zone(Node(NodeType.Cdn, """{"zone":"a"}""")));
        Assert.Null(NodeSettings.Zone(Node(NodeType.ExternalService, """{"zone":"a"}""")));
        Assert.Null(NodeSettings.Zone(Node(NodeType.Client, """{"zone":"a"}""")));
    }

    private static Node Node(NodeType type, string configuration) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Name = type.ToString(),
        Configuration = configuration
    };
}
