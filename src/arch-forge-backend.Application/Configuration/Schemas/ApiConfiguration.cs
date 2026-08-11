namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V3 D3.5: an editable field with no consequence is a broken promise. Protocol duplicated
/// <see cref="Domain.Entities.Connection.Protocol"/>, and Timeout/Retries were V1 leftovers that
/// nobody has read since V2 D3.1 moved resilience onto the edge. Nothing survived.
///
/// The class stays because the registry still has to answer "is this a JSON object" for an Api
/// node, and because the day a rule earns a field back, it lands here.
/// </summary>
public class ApiConfiguration
{
}
