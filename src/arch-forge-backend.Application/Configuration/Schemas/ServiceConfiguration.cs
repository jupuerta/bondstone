namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V3 D3.4 and D3.5. Instances is gone because redundancy is topological and nothing else:
/// two boxes behind a balancer teach, <c>instances: 3</c> does not. Timeout and Retries were V1
/// leftovers duplicating the edge's resilience.
/// </summary>
public class ServiceConfiguration
{
}
