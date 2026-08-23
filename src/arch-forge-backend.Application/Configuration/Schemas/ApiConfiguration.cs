namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V4 DT.8. V3 emptied this class because an editable field with no consequence is a broken
/// promise (D3.5); V4 gives two back, each with the mechanism that earns it.
/// </summary>
public class ApiConfiguration
{
    /// <summary>
    /// DT.4. Multiplies capacity, so <c>instances: 3</c> is three times the requests before
    /// saturating. V3 retired it saying "two boxes behind a balancer teach, <c>instances: 3</c> does
    /// not" - which was true while nothing counted requests, and stopped being true here.
    /// </summary>
    public int? Instances { get; set; }

    /// <summary>
    /// DT.1, the axis itself. Only means anything on an entry point; anywhere else the load is
    /// whatever arrives. Absent is <strong>zero declared load</strong>, and zero load is what makes
    /// every architecture drawn before V4 behave exactly as it did (DT.6).
    /// </summary>
    public int? RequestsPerSecond { get; set; }

    /// <summary>
    /// QA §4. The same lever <see cref="GatewayConfiguration.RateLimit"/> is, on the other thing
    /// that can be a front door. Until it existed, an Api with declared traffic was a door
    /// NO_RATE_LIMIT could never speak to: the rule asked only gateways, because only gateways had
    /// the field. Read by that rule and by the load model's admission (DT.3), which is what earns
    /// it the place (D3.5).
    /// </summary>
    public int? RateLimit { get; set; }

    /// <summary>
    /// V4 D5.2. An Api can be the front door too, so it is asked the same question a Gateway is.
    ///
    /// V13 D2.1. <strong>Nullable, and the null is the whole point.</strong> As a
    /// <see cref="bool"/> this field could not tell "I have not configured it yet" from "I have
    /// decided it carries none", so <c>UNAUTHENTICATED_ENTRY</c> opened every new diagram with a
    /// Critical - the "todo rojo" the project has spent versions avoiding, only moved to Security.
    /// Three states, three answers (D2.1).
    /// </summary>
    public bool? Authentication { get; set; }
}
