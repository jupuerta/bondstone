using System.Text.Json;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Configuration;

public class NodeConfigurationProvider<TConfiguration> : INodeConfigurationProvider
    where TConfiguration : class
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NodeConfigurationProvider(NodeType nodeType)
    {
        NodeType = nodeType;
    }

    public NodeType NodeType { get; }
    public Type ConfigurationType => typeof(TConfiguration);

    public object Deserialize(JsonElement configuration) =>
        JsonSerializer.Deserialize<TConfiguration>(configuration.GetRawText(), SerializerOptions)
            ?? throw new JsonException($"Configuration could not be deserialized as {typeof(TConfiguration).Name}.");
}
