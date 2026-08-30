using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Enums;
using Xunit;

namespace arch_forge_backend.Application.Tests.Analysis.Rules;

public class NodeRolesTests
{
    // The sixteen rows of Anexo B of ArchForge_zonas_V20.md: size, pay, place are three
    // different sets, and the Cdn row (billed, never placed) is what keeps them from collapsing.
    public static TheoryData<NodeType, bool, bool, bool> AnnexB => new()
    {
        { NodeType.Api, true, true, true },
        { NodeType.Service, true, true, true },
        { NodeType.Worker, true, true, true },
        { NodeType.Database, true, true, true },
        { NodeType.ObjectStorage, true, true, true },
        { NodeType.Cache, true, true, true },
        { NodeType.Queue, true, true, true },
        { NodeType.Topic, true, true, true },
        { NodeType.Gateway, true, true, true },
        { NodeType.LoadBalancer, true, true, true },
        { NodeType.Observability, false, true, true },
        // QA 30-08 §10.3, point 14: you pay an identity SaaS, you do not put it in your aisle of the
        // data centre — which is what the manual has always said, and what SPOF's advice assumes.
        { NodeType.IdentityProvider, false, true, false },
        { NodeType.Scheduler, false, true, true },
        { NodeType.Cdn, false, true, false },
        { NodeType.ExternalService, false, false, false },
        { NodeType.Client, false, false, false }
    };

    [Theory]
    [MemberData(nameof(AnnexB))]
    public void The_three_ownership_predicates_are_three_different_sets(
        NodeType type, bool yoursToSize, bool yoursToPayFor, bool yoursToPlace)
    {
        Assert.Equal(yoursToSize, NodeRoles.IsYoursToSize(type));
        Assert.Equal(yoursToPayFor, NodeRoles.IsYoursToPayFor(type));
        Assert.Equal(yoursToPlace, NodeRoles.IsYoursToPlace(type));
    }
}
