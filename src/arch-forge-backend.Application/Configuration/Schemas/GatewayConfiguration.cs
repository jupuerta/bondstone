namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V4 DT.8. V3 retired <c>RateLimit</c> with a sentence that reads like a promissory note - "a
/// defence against load, and there is no model of load (D3.3)". Now there is one, so it comes back
/// and it defends: it caps the traffic that gets in (DT.3).
/// </summary>
public class GatewayConfiguration
{
    /// <summary>DT.1. Only means anything on an entry point.</summary>
    public int? RequestsPerSecond { get; set; }

    /// <summary>
    /// DT.3. The admitted load is <c>min(declared, rateLimit)</c> - the requests over the limit are
    /// rejected at the door instead of piling onto whatever is downstream. It is the cheapest
    /// lesson in the model: one number turns a cascade into a rejection.
    /// </summary>
    public int? RateLimit { get; set; }

    /// <summary>
    /// V4 D5.2. Comes back with <c>UNAUTHENTICATED_ENTRY</c>, the rule that reads it - which is the
    /// principle V3 D3.5 fixed and the reason it was not brought back in VS2 along with the rest.
    /// </summary>
    public bool Authentication { get; set; }
}
