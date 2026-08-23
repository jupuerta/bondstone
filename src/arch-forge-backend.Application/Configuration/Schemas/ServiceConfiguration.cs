namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V4 DT.8. <c>Instances</c> comes back for the reason V3 D3.4 did not have: it multiplies
/// capacity (DT.4). Timeout and Retries stay gone - they were V1 leftovers duplicating the edge's
/// resilience, and nothing in V4 gives them a job.
///
/// V14 B6 (N12). <c>Authentication</c> arrives because a Service can be a door - a BFF facing the
/// Client is the ordinary case - and a door gets asked whether it checks who is calling
/// (<c>UNAUTHENTICATED_ENTRY</c>). Until V14 the frontier walked straight through a Service and
/// found the database behind it "reachable without passing through any API", with advice to
/// rename the box; the field is what lets the Service answer the question instead. Three states
/// like the Api's (V13 D2.1): <c>null</c> is "nobody has said", which the rule grades differently
/// from "no".
/// </summary>
public class ServiceConfiguration
{
    public int? Instances { get; set; }
    public bool? Authentication { get; set; }
}
