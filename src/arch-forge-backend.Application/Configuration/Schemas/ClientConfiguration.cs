namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// QA §3.1. The one thing the outside declares: how much of it there is.
/// </summary>
public class ClientConfiguration
{
    /// <summary>
    /// DT.1, moved to where the traffic is actually born. Declaring it on the Api or the Gateway
    /// keeps working - a diagram without a Client is still a diagram - but with a Client drawn,
    /// this is the honest place for the number.
    /// </summary>
    public int? RequestsPerSecond { get; set; }
}
