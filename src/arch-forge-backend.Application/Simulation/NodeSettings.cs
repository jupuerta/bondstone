using System.Text.Json;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// The two node fields V3 activates (D3.5), read the same forgiving way
/// <see cref="Analysis.ArchitectureGraph.ResilienceOf"/> reads the edge's: saving already validated
/// the schema, so an unreadable configuration means "nothing configured" rather than a failed
/// simulation.
/// </summary>
public static class NodeSettings
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>D4.7: consumer redeliveries. 0 when there are none.</summary>
    public static int QueueRetries(Node node) =>
        node.Type == NodeType.Queue ? Read<QueueConfiguration>(node)?.MaxRetries ?? 0 : 0;

    /// <summary>D4.8: the field that decides whether a dead instance stops at the balancer or climbs past it.</summary>
    public static bool HasHealthCheck(Node node) =>
        node.Type == NodeType.LoadBalancer && Read<LoadBalancerConfiguration>(node)?.HealthCheck == true;

    private static T? Read<T>(Node node) where T : class
    {
        if (string.IsNullOrWhiteSpace(node.Configuration))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(node.Configuration, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
