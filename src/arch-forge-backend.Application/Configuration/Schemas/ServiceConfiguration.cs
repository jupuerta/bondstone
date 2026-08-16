namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V4 DT.8. <c>Instances</c> comes back for the reason V3 D3.4 did not have: it multiplies
/// capacity (DT.4). Timeout and Retries stay gone - they were V1 leftovers duplicating the edge's
/// resilience, and nothing in V4 gives them a job.
/// </summary>
public class ServiceConfiguration
{
    public int? Instances { get; set; }
}
