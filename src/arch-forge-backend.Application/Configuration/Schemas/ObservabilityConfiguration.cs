namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V4 D5.3. Nothing to configure yet, and that is the point: <c>NO_OBSERVABILITY</c> asks whether
/// the system can be observed at all, which is a question about the graph and not about a field.
/// The class exists because the registry has to answer "is this a JSON object" for the type, and
/// because the day a rule earns a field, it lands here.
/// </summary>
public class ObservabilityConfiguration
{
}
