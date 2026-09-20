using System.Text.Json;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Configuration;

public interface INodeConfigurationProvider
{
    NodeType NodeType { get; }
    Type ConfigurationType { get; }
    object Deserialize(JsonElement configuration);
}
