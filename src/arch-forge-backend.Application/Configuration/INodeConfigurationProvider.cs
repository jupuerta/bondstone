using System.Text.Json;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Configuration;

public interface INodeConfigurationProvider
{
    NodeType NodeType { get; }
    Type ConfigurationType { get; }
    object Deserialize(JsonElement configuration);
}
