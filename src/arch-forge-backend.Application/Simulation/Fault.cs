using System.Text.Json.Serialization;
using bondstone_backend.Application.Analysis;

namespace bondstone_backend.Application.Simulation;

public sealed record Fault(ElementType Target, Guid Id, string Kind, int? Value = null)
{
    [JsonIgnore]
    public FindingElement Element => new(Target, Id);
}
