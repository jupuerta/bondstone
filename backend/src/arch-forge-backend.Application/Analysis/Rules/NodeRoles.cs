using bondstone_backend.Domain.Entities;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Analysis.Rules;

public static class NodeRoles
{
    public static bool IsSystemOfRecord(NodeType type) =>
        type is NodeType.Database or NodeType.ObjectStorage;

    public static bool IsLogicComponent(NodeType type) =>
        type is NodeType.Api or NodeType.Service or NodeType.Worker
             or NodeType.ExternalService or NodeType.IdentityProvider;

    public static bool ProcessesMessages(NodeType type) =>
        type is NodeType.Worker or NodeType.Service or NodeType.Api or NodeType.ExternalService;

    public static bool IsRouter(NodeType type) =>
        type is NodeType.Gateway or NodeType.LoadBalancer;

    // A router's call that carries the client's request onwards, as opposed to the router's own
    // question — «who is calling?», to an identity provider — which is the one piece of work a
    // gateway does itself and the one call of its where a fallback (the sessions already open, a
    // token cache) is real. Everything a router forwards it cannot answer for (QA 30-08, 7.4).
    public static bool ForwardsRequest(ArchitectureGraph graph, Connection edge) =>
        graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && IsRouter(source.Type)
        && graph.NodesById.TryGetValue(edge.TargetNodeId, out var target) && target.Type != NodeType.IdentityProvider;

    public static bool IsDataStore(NodeType type) =>
        IsSystemOfRecord(type) || type is NodeType.Cache;

    public static bool IsTrafficOrigin(NodeType type) => type is NodeType.Client;

    public static bool IsRequestFromOutside(ArchitectureGraph graph, Connection edge) =>
        graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && IsTrafficOrigin(source.Type);

    public static bool IsCallByThirdParty(ArchitectureGraph graph, Connection edge) =>
        graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && source.Type == NodeType.ExternalService;

    public static bool IsNotYourCall(ArchitectureGraph graph, Connection edge) =>
        IsRequestFromOutside(graph, edge) || IsCallByThirdParty(graph, edge);

    public static bool ActsFromInside(NodeType type) =>
        type is NodeType.Worker or NodeType.Observability or NodeType.Scheduler;

    public static bool ReceivesFromOutside(NodeType type) =>
        type is NodeType.Api or NodeType.Gateway or NodeType.LoadBalancer
             or NodeType.Cdn or NodeType.ExternalService;

    public static bool CanAuthenticate(NodeType type) =>
        type is NodeType.Api or NodeType.Gateway or NodeType.Service;

    public static bool IsTelemetry(NodeType type) => type is NodeType.Observability;

    // Where work can wait: the one place an arrow that does not wait still leaves something behind.
    public static bool IsBroker(NodeType type) => type is NodeType.Queue or NodeType.Topic;

    public static bool IsTelemetryEdge(ArchitectureGraph graph, Connection edge) =>
        (graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && IsTelemetry(source.Type))
        || (graph.NodesById.TryGetValue(edge.TargetNodeId, out var target) && IsTelemetry(target.Type));

    public static bool IsObservable(NodeType type) =>
        !IsTelemetry(type)
        && !IsTrafficOrigin(type)
        && type is not (NodeType.ExternalService or NodeType.Cdn or NodeType.IdentityProvider);

    public static bool IsYoursToSize(NodeType type) =>
        IsObservable(type) && type is not NodeType.Scheduler;

    // Three different sets (zonas V20, Anexo B). IsYoursToSize excludes Observability,
    // IdentityProvider and Scheduler, and you pay for all three; the Cdn and the identity provider
    // are the rows that keep pay and place apart: their bill reaches you, but a CDN's definition is
    // being everywhere, and an identity SaaS is not in your aisle of the data centre — which is what
    // the manual has always said and what SPOF.suggestion.identityProvider assumes when it says you
    // cannot replicate it (QA 30-08, §10.3 point 14).
    public static bool IsYoursToPayFor(NodeType type) =>
        type is not (NodeType.ExternalService or NodeType.Client);

    public static bool IsYoursToPlace(NodeType type) =>
        IsYoursToPayFor(type) && !IsProviderEdge(type) && type is not NodeType.IdentityProvider;

    public static bool IsProviderEdge(NodeType type) => type is NodeType.Cdn;

    public static (Guid Anchor, NodeType Type) ReplicaSetOf(ArchitectureGraph graph, Node node)
    {
        var balancer = graph.IncomingByTarget[node.Id]
            .Select(edge => graph.NodesById.GetValueOrDefault(edge.SourceNodeId))
            .Where(source => source is { Type: NodeType.LoadBalancer })
            .Where(source => graph.OutgoingBySource[source!.Id]
                .Select(edge => graph.NodesById.GetValueOrDefault(edge.TargetNodeId))
                .Where(target => target?.Type == node.Type)
                .Select(target => target!.Id)
                .Distinct()
                .Count() >= 2)
            .OrderBy(source => source!.Id)
            .FirstOrDefault();

        return (balancer?.Id ?? node.Id, node.Type);
    }

    public static bool IsTelemetryScrape(ArchitectureGraph graph, Connection edge) =>
        graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && IsTelemetry(source.Type);

    public static bool IsTelemetryPush(ArchitectureGraph graph, Connection edge) =>
        !IsTelemetryScrape(graph, edge)
        && graph.NodesById.TryGetValue(edge.TargetNodeId, out var target) && IsTelemetry(target.Type);

    public static bool MustSitBehindADoor(NodeType type) =>
        IsYoursToSize(type) && !CanAuthenticate(type) && type is not NodeType.LoadBalancer;
}
