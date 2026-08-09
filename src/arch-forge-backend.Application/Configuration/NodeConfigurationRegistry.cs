using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Configuration;

public class NodeConfigurationRegistry
{
    private readonly Dictionary<NodeType, INodeConfigurationProvider> _providers;

    public NodeConfigurationRegistry(IEnumerable<INodeConfigurationProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.NodeType);
    }

    public bool TryGetProvider(NodeType nodeType, out INodeConfigurationProvider provider) =>
        _providers.TryGetValue(nodeType, out provider!);
}
